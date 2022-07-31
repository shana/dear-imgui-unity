using System.Collections;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Profiling;
using UnityEditor;
using Debug = UnityEngine.Debug;

namespace ImGuiNET.Unity
{
    // This component is responsible for setting up ImGui for use in Unity.
    // It holds the necessary context and sets it up before any operation is done to ImGui.
    // (e.g. set the context, texture and font managers before rendering)

    /// <summary>
    /// Dear ImGui integration into Unity
    /// </summary>
    public class DearImGui : MonoBehaviour
    {
        ImGuiUnityContext _context;
        IImGuiRenderer _renderer;
        IImGuiPlatform _platform;
        CommandBuffer _cmd;
        bool _usingURP;
        bool _renderDeferred;
        bool _frameReady;

        protected bool FramePrepared { get; set; }

        protected bool FrameReady
        {
            get => _frameReady && IsRepaint;
            set { _frameReady = value; }
        }

        private bool IsRepaint => Event.current == null || Event.current.type == EventType.Repaint;

        protected bool FrameBegun { get; set; }

        [SerializeField] Camera _camera = null;
        [SerializeField] RenderImGuiFeature _renderFeature = null;

        [SerializeField] RenderUtils.RenderType _rendererType = RenderUtils.RenderType.Mesh;
        [SerializeField] Platform.Type _platformType = Platform.Type.InputManager;

        [Header("Configuration")]
        [SerializeField] IOConfig _initialConfiguration = default;
        [SerializeField] FontAtlasConfigAsset _fontAtlasConfiguration = null;
        [SerializeField] IniSettingsAsset _iniSettings = null;  // null: uses default imgui.ini file

        [Header("Customization")]
        [SerializeField] ShaderResourcesAsset _shaders = null;
        [SerializeField] StyleAsset _style = null;
        [SerializeField] CursorShapesAsset _cursorShapes = null;

        const string CommandBufferTag = "DearImGui";
        static readonly ProfilerMarker s_prepareFramePerfMarker = new ProfilerMarker("DearImGui.PrepareFrame");
        static readonly ProfilerMarker s_layoutPerfMarker = new ProfilerMarker("DearImGui.Layout");
        static readonly ProfilerMarker s_drawListPerfMarker = new ProfilerMarker("DearImGui.RenderDrawLists");

        protected virtual void Awake()
        {
            Ensure();
        }

        protected virtual void OnDestroy()
        {
            if (_context != null)
                ImGuiUn.DestroyUnityContext(_context);
            _context = null;
        }

        protected virtual void OnEnable()
        {
            Ensure();

            _usingURP = RenderUtils.IsUsingURP();
            if (_camera == null) Fail(nameof(_camera));
            if (_renderFeature == null && _usingURP) Fail(nameof(_renderFeature));

            _cmd = RenderUtils.GetCommandBuffer(CommandBufferTag);
            if (_usingURP)
                _renderFeature.commandBuffer = _cmd;
            else
                _camera.AddCommandBuffer(CameraEvent.AfterEverything, _cmd);

            ActivateContext();
            ImGuiIOPtr io = ImGui.GetIO();

            _initialConfiguration.ApplyTo(io);
            _style?.ApplyTo(ImGui.GetStyle());

            _context.textures.BuildFontAtlas(io, _fontAtlasConfiguration);
            _context.textures.Initialize(io);

            SetPlatform(Platform.Create(_platformType, _cursorShapes, _iniSettings), io);
            SetRenderer(RenderUtils.Create(_rendererType, _shaders, _context.textures), io);
            if (_platform == null) Fail(nameof(_platform));
            if (_renderer == null) Fail(nameof(_renderer));

            void Fail(string reason)
            {
                enabled = false;
                throw new System.Exception($"Failed to start: {reason}");
            }
        }

        protected virtual void OnDisable()
        {
            ImGuiIOPtr io;
            if (_context != null)
            {
                // end any frame that's pending
                EndFrame();

                ActivateContext();
                io = ImGui.GetIO();

                SetRenderer(null, io);
                SetPlatform(null, io);
            }

            DeactivateContext();

            _context?.textures.Shutdown();
            _context?.textures.DestroyFontAtlas(io);

            if (_usingURP)
            {
                if (_renderFeature != null)
                    _renderFeature.commandBuffer = null;
            }
            else
            {
                if (_camera != null)
                    _camera.RemoveCommandBuffer(CameraEvent.AfterEverything, _cmd);
            }

            if (_cmd != null)
                RenderUtils.ReleaseCommandBuffer(_cmd);
            _cmd = null;
        }

        protected virtual void Reset()
        {
            _camera = Camera.main;
            _initialConfiguration.SetDefaults();
            _shaders = Resources.FindObjectsOfTypeAll<ShaderResourcesAsset>().FirstOrDefault();
            _style = Resources.FindObjectsOfTypeAll<StyleAsset>().FirstOrDefault();
            _cursorShapes = Resources.FindObjectsOfTypeAll<CursorShapesAsset>().FirstOrDefault();
        }

        private void Ensure()
        {
            if (_context == null)
                _context = ImGuiUn.CreateUnityContext();
        }

        public void Reload()
        {
            OnDisable();
            OnEnable();
        }

        public void EndFrame()
        {
            if (!FrameBegun) return;

            Render();
        }

        public void EnsureFrame()
        {
            if (!enabled)
                return;

            if (!FramePrepared)
            {
                PrepareFrame();
            }

            // can only pump events while we aren't in a frame
            if (!FrameBegun)
            {
                ActivateContext();
                FrameReady = _platform.UpdateInput(ImGui.GetIO());
            }

            // either all unity ongui event input has been handled, or we're using the input system, which has all the input set up already
            if (FrameReady)
            {
                NewFrame();
            }
        }

        void PrepareFrame()
        {
            FramePrepared = true;

            ActivateContext();
            ImGuiIOPtr io = ImGui.GetIO();

            ProfilerPrepareBegin();

            _context.textures.PrepareFrame(io);
            _platform.PrepareFrame(io, _camera.pixelRect);

            ProfilerPrepareEnd();
        }

        void NewFrame()
        {
            if (FrameBegun)
                return;
            FrameBegun = true;

            ProfilerLayoutBegin();

            ImGui.NewFrame();

            if (!_renderDeferred)
            {
                _renderDeferred = true;
                if (!Application.isPlaying)
                {
#if UNITY_EDITOR
                    EditorApplication.update += EditorApplicationUpdate;
#endif
                }
                else
                {
                    StartCoroutine(RenderOnFrameEnd());
                }
            }
        }

#if UNITY_EDITOR
        void EditorApplicationUpdate()
        {
            EditorApplication.update -= EditorApplicationUpdate;
            EndFrame();
        }
#endif

        IEnumerator RenderOnFrameEnd()
        {
            yield return new WaitForEndOfFrame();
            EndFrame();
        }

        void Render()
        {
            FrameBegun = false;
            FrameReady = false;
            FramePrepared = false;
            _renderDeferred = false;

            ActivateContext();

            ImGui.Render();

            ProfilerLayoutEnd();

            ProfilerDrawBegin();

            _cmd.Clear();
            _renderer.RenderDrawLists(_cmd, ImGui.GetDrawData());

            ProfilerDrawEnd();
        }

        void SetRenderer(IImGuiRenderer renderer, ImGuiIOPtr io)
        {
            _renderer?.Shutdown(io);
            _renderer = renderer;
            _renderer?.Initialize(io);
        }

        void SetPlatform(IImGuiPlatform platform, ImGuiIOPtr io)
        {
            _platform?.Shutdown(io);
            _platform = platform;
            _platform?.Initialize(io);
        }

        void ActivateContext() => ImGuiUn.SetUnityContext(_context);
        void DeactivateContext() => ImGuiUn.SetUnityContext(null);


        [Conditional("ENABLE_PROFILER")]
        private void ProfilerPrepareBegin()
        {
            s_prepareFramePerfMarker.Begin(this);
        }

        [Conditional("ENABLE_PROFILER")]
        private void ProfilerPrepareEnd()
        {
            s_prepareFramePerfMarker.End();
        }

        [Conditional("ENABLE_PROFILER")]
        private void ProfilerLayoutBegin()
        {
            s_layoutPerfMarker.Begin(this);
        }

        [Conditional("ENABLE_PROFILER")]
        private static void ProfilerDrawEnd()
        {
            s_drawListPerfMarker.End();
        }

        [Conditional("ENABLE_PROFILER")]
        private static void ProfilerLayoutEnd()
        {
            s_layoutPerfMarker.End();
        }

        [Conditional("ENABLE_PROFILER")]
        private void ProfilerDrawBegin()
        {
            s_drawListPerfMarker.Begin(this);
        }
    }
}
