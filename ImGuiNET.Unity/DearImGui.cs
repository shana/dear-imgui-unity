using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Profiling;

namespace ImGuiNET.Unity
{
    // This component is responsible for setting up ImGui for use in Unity.
    // It holds the necessary context and sets it up before any operation is done to ImGui.
    // (e.g. set the context, texture and font managers before calling Layout)

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
        protected bool FrameBegun { get; private set; }

        public event System.Action Layout;  // Layout event for *this* ImGui instance
        [SerializeField] bool _doGlobalLayout = true; // do global/default Layout event too

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

            ImGuiUn.SetUnityContext(_context);
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

        private void Ensure()
        {
            if (_context == null)
                _context = ImGuiUn.CreateUnityContext();
        }

        protected virtual void OnDisable()
        {
            ImGuiIOPtr io;
            if (_context != null)
            {
                ImGuiUn.SetUnityContext(_context);
                io = ImGui.GetIO();

                SetRenderer(null, io);
                SetPlatform(null, io);
            }

            ImGuiUn.SetUnityContext(null);

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
        }

        public void Reload()
        {
            OnDisable();
            OnEnable();
        }

        protected virtual void LateUpdate()
        {
            ImGuiUn.SetUnityContext(_context);
            
            if ((_doGlobalLayout && ImGuiUn.NeedsFrame) || Layout != null)
            {
                EnsureFrame();
            }

            try
            {
                if (_doGlobalLayout)
                    ImGuiUn.DoLayout();   // ImGuiUn.Layout: global handlers
                Layout?.Invoke();     // this.Layout: handlers specific to this instance
            }
            finally
            {
                Render();
            }
        }

        void Render()
        {
            if (!FrameBegun) return;
            FrameBegun = false;

            ImGui.Render();

            ProfilerLayoutEnd();
            ProfilerDrawBegin();

            _cmd.Clear();
            _renderer.RenderDrawLists(_cmd, ImGui.GetDrawData());

            ProfilerDrawEnd();
        }


        public void EnsureFrame()
        {
            if (FrameBegun || !enabled)
                return;
 
            PrepareFrame();
        }


        void PrepareFrame()
        {
            FrameBegun = true;

            ImGuiUn.SetUnityContext(_context);
            ImGuiIOPtr io = ImGui.GetIO();

            ProfilerPrepareBegin();

            _context.textures.PrepareFrame(io);
            _platform.PrepareFrame(io, _camera.pixelRect);
            ImGui.NewFrame();

            ProfilerPrepareEnd();

            ProfilerLayoutBegin();
        }

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
    }
}
