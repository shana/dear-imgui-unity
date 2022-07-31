using System;
using System.Collections.Generic;
using System.Data;
using System.Runtime.CompilerServices;
using System.Text;
using Mono.Cecil;
using UnityEditor;
using UnityEditor.PackageManager.UI;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.InputSystem;

namespace ImGuiNET.Unity
{
    // Implemented features:
    // [x] Platform: Clipboard support.
    // [x] Platform: Mouse cursor shape and visibility. Disable with io.ConfigFlags |= ImGuiConfigFlags.NoMouseCursorChange.
    // [x] Platform: Keyboard arrays indexed using KeyCode codes, e.g. ImGui.IsKeyPressed(KeyCode.Space).
    // [ ] Platform: Gamepad support. Enabled with io.ConfigFlags |= ImGuiConfigFlags.NavEnableGamepad.
    // [~] Platform: IME support.
    // [~] Platform: INI settings support.

    /// <summary>
    /// Platform bindings for ImGui in Unity in charge of: mouse/keyboard/gamepad inputs, cursor shape, timing, windowing.
    /// </summary>
    sealed class ImGuiPlatformInputManager : IImGuiPlatform
    {
        readonly Event _e = new Event(); // to get text input

        readonly CursorShapesAsset _cursorShapes; // cursor shape definitions
        ImGuiMouseCursor _lastCursor = ImGuiMouseCursor.COUNT; // last cursor requested by ImGui

        readonly IniSettingsAsset _iniSettings; // ini settings data

        readonly PlatformCallbacks _callbacks = new PlatformCallbacks
        {
            GetClipboardText = (_) => GUIUtility.systemCopyBuffer,
            SetClipboardText = (_, text) => GUIUtility.systemCopyBuffer = text,
#if IMGUI_FEATURE_CUSTOM_ASSERT
            LogAssert =
 (condition, file, line) => Debug.LogError($"[DearImGui] Assertion failed: '{condition}', file '{file}', line: {line}."),
            DebugBreak = () => System.Diagnostics.Debugger.Break(),
#endif
        };

        readonly Dictionary<int, int> keyMap = new Dictionary<int, int>
        {
            {(int) KeyCode.Tab, (int) ImGuiKey.Tab},
            {(int) KeyCode.LeftArrow, (int) ImGuiKey.LeftArrow},
            {(int) KeyCode.RightArrow, (int) ImGuiKey.RightArrow},
            {(int) KeyCode.UpArrow, (int) ImGuiKey.UpArrow},
            {(int) KeyCode.DownArrow, (int) ImGuiKey.DownArrow},
            {(int) KeyCode.PageUp, (int) ImGuiKey.PageUp},
            {(int) KeyCode.PageDown, (int) ImGuiKey.PageDown},
            {(int) KeyCode.Home, (int) ImGuiKey.Home},
            {(int) KeyCode.End, (int) ImGuiKey.End},
            {(int) KeyCode.Insert, (int) ImGuiKey.Insert},
            {(int) KeyCode.Delete, (int) ImGuiKey.Delete},
            {(int) KeyCode.Backspace, (int) ImGuiKey.Backspace},
            {(int) KeyCode.Space, (int) ImGuiKey.Space},
            {(int) KeyCode.Return, (int) ImGuiKey.Enter},
            {(int) KeyCode.Escape, (int) ImGuiKey.Escape},
            {(int) KeyCode.KeypadEnter, (int) ImGuiKey.KeypadEnter},
            {(int) KeyCode.A, (int) ImGuiKey.A}, // for text edit CTRL+A: select all
            {(int) KeyCode.C, (int) ImGuiKey.C}, // for text edit CTRL+C: copy
            {(int) KeyCode.V, (int) ImGuiKey.V}, // for text edit CTRL+V: paste
            {(int) KeyCode.X, (int) ImGuiKey.X}, // for text edit CTRL+X: cut
            {(int) KeyCode.Y, (int) ImGuiKey.Y}, // for text edit CTRL+Y: redo
            {(int) KeyCode.Z, (int) ImGuiKey.Z}, // for text edit CTRL+Z: undo
        };


        public ImGuiPlatformInputManager(CursorShapesAsset cursorShapes, IniSettingsAsset iniSettings)
        {
            _cursorShapes = cursorShapes;
            _iniSettings = iniSettings;
            _callbacks.ImeSetPlatformImeData = (__, imeDataPtr) => Input.compositionCursorPos = imeDataPtr.InputPos;
        }

        public bool Initialize(ImGuiIOPtr io)
        {
            io.SetBackendPlatformName("Unity Input Manager"); // setup backend info and capabilities
            io.BackendFlags |= ImGuiBackendFlags.HasMouseCursors; // can honor GetMouseCursor() values
            io.BackendFlags &= ~ImGuiBackendFlags.HasSetMousePos; // can't honor io.WantSetMousePos requests
            // io.BackendFlags |= ImGuiBackendFlags.HasGamepad;                 // set by UpdateGamepad()

            _callbacks.Assign(io); // assign platform callbacks
            io.ClipboardUserData = IntPtr.Zero;

            if (_iniSettings != null) // ini settings
            {
                io.SetIniFilename(null); // handle ini saving manually
                ImGui.LoadIniSettingsFromMemory(_iniSettings
                    .Load()); // call after CreateContext(), before first call to NewFrame()
            }

            SetupKeyboard(io); // sets key mapping, text input, and IME

            return true;
        }

        public void Shutdown(ImGuiIOPtr io)
        {
            _callbacks.Unset(io);
            io.SetBackendPlatformName(null);
        }

        int controlID = -1;

        public bool UpdateInput(ImGuiIOPtr io)
        {
            if (!Application.isPlaying && Event.current != null)
            {
                if (controlID == -1)
                    controlID = GUIUtility.GetControlID(FocusType.Keyboard);
            }
            return UpdateInputInternal(io);
        }

        private StateInfo TryGetStateInfo()
        {
            StateInfo state = null;

            // if we're in an OnGUI callback, we need to process all events before we can call ImGui, and
            // there are multiple OnGUI callbacks in one frame, one per event (sometimes more)
            // we will only do ImGui when Repaint happens, we'll accumulate a state object of everything
            // that happens until then here
            if (Event.current != null)
            {
                // Get (or create) the state object
                state = (StateInfo) GUIUtility.GetStateObject(typeof(StateInfo), controlID);
            }

            return state;
        }

        private bool UpdateInputInternal(ImGuiIOPtr io)
        {
            var ret = Application.isPlaying || Event.current == null;

            var state = TryGetStateInfo();
            if (!Application.isPlaying && state != null)
            {
                var eventInfo = state.Process(controlID);

                if (!Application.isPlaying)
                {
                    if (eventInfo.IsKey)
                    {
                        io.AddKeyEvent((ImGuiKey) keyMap[(int) state.Key], state.IsKeyDown);
                    }

                    if (eventInfo.IsMouse)
                    {
                        if (eventInfo.IsMouseDown || eventInfo.IsMouseUp)
                        {
                            io.AddMouseButtonEvent(state.Button, eventInfo.IsMouseDown);
                        }

                        if (state.IsScrollWheel)
                        {
                            io.AddMouseWheelEvent(eventInfo.Scroll.y, eventInfo.Scroll.x);
                        }

                    }
                }

                ret = state.ReadyToRender;

                if (!Application.isPlaying)
                {
                    if (ret)
                    {
                        var pos = state.MousePosition;
                        io.AddMousePosEvent(pos.x, pos.y);
                    }
                }
            }

            if (ret)
            {
                // input
                UpdateKeyboard(io); // update keyboard state
                UpdateMouse(io); // update mouse state
                UpdateCursor(io, ImGui.GetMouseCursor()); // update Unity cursor with the cursor requested by ImGui
            }
            
            return ret;
        }

        public void PrepareFrame(ImGuiIOPtr io, Rect displayRect)
        {
            Assert.IsTrue(io.Fonts.IsBuilt(),
                "Font atlas not built! Generally built by the renderer. Missing call to renderer NewFrame() function?");

            io.DisplaySize = new Vector2(displayRect.width, displayRect.height); // setup display size (every frame to accommodate for window resizing)
            // TODO: dpi aware, scale, etc

            io.DeltaTime = Time.unscaledDeltaTime; // setup timestep

            var state = TryGetStateInfo();
            state?.Clear();
            //io.MouseDown[0] = io.MouseDown[1] = io.MouseDown[2] = false;

            // ini settings
            if (_iniSettings != null && io.WantSaveIniSettings)
            {
                _iniSettings.Save(ImGui.SaveIniSettingsToMemory());
                io.WantSaveIniSettings = false;
            }
        }


        void SetupKeyboard(ImGuiIOPtr io)
        {
        }

        void UpdateKeyboard(ImGuiIOPtr io)
        {
            if (!Application.isPlaying || !Input.anyKey)
                return;

            // main keys
            foreach (var key in keyMap)
            {
                var keyCode = (KeyCode) key.Key;
                if (Input.GetKey(keyCode))
                    io.AddKeyEvent((ImGuiKey) key.Value, Input.GetKeyDown(keyCode));
            }

            // keyboard modifiers
            io.KeyShift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            io.KeyCtrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            io.KeyAlt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            io.KeySuper = Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand)
                                                            || Input.GetKey(KeyCode.LeftWindows) ||
                                                            Input.GetKey(KeyCode.RightWindows);
        }

        static void UpdateMouse(ImGuiIOPtr io)
        {
            if (!Application.isPlaying)
                return;

            var pos = ImGuiUn.ScreenToImGui(new Vector2(Input.mousePosition.x, Input.mousePosition.y));
            io.AddMousePosEvent(pos.x, pos.y);

            if (!Input.anyKey)
                return;

            for (var i = 0; i < 3; i++)
            {
                if (Input.GetMouseButton(i))
                    io.AddMouseButtonEvent(i, Input.GetMouseButtonDown(i));
            }
        }

        void UpdateCursor(ImGuiIOPtr io, ImGuiMouseCursor cursor)
        {
            if (io.MouseDrawCursor)
                cursor = ImGuiMouseCursor.None;

            if (_lastCursor == cursor)
                return;
            if ((io.ConfigFlags & ImGuiConfigFlags.NoMouseCursorChange) != 0)
                return;

            _lastCursor = cursor;
            Cursor.visible =
                cursor != ImGuiMouseCursor.None; // hide cursor if ImGui is drawing it or if it wants no cursor
            if (_cursorShapes != null)
                Cursor.SetCursor(_cursorShapes[cursor].texture, _cursorShapes[cursor].hotspot, CursorMode.Auto);
        }
    }

    struct UnityEventInfo
    {
        public bool IsMouseDown { get; private set; }
        public bool IsMouseUp { get; private set; }
        public bool IsScrollWheel { get; private set; }
        public bool IsKeyDown { get; private set; }
        public bool IsKeyUp { get; private set; }
        public bool IsKey { get; private set; }
        public bool IsMouse { get; private set; }
        public Vector2 MousePosition { get; private set; }
        public bool MousePositionUpdated { get; private set; }

        public Vector2 Scroll { get; private set; }
        public int Button { get; private set; }
        public KeyCode Key { get; private set; }
        public bool ReadyToRender { get; private set; }


        public UnityEventInfo Process(int controlID)
        {
            //if (GUIUtility.hotControl != controlID)
            //    return;

            var ev = Event.current;
            var eventType = ev.GetTypeForControl(controlID);

            ReadyToRender = ev.type == EventType.Repaint;

            switch (eventType)
            {
                case EventType.MouseDown:
                    IsMouse = IsMouseDown = true;
                    Button = ev.button;
                    Event.current.Use();
                    break;
                case EventType.MouseUp:
                    IsMouse = IsMouseUp = true;
                    Button = ev.button;
                    Event.current.Use();
                    break;
                case EventType.MouseMove:
                    break;
                case EventType.MouseDrag:
                    break;
                case EventType.KeyDown:
                    IsKey = IsKeyDown = true;
                    Key = ev.keyCode;
                    Event.current.Use();
                    break;
                case EventType.KeyUp:
                    IsKey = IsKeyUp = true;
                    Key = ev.keyCode;
                    Event.current.Use();
                    break;
                case EventType.ScrollWheel:
                    IsScrollWheel = true;
                    Scroll = ev.delta;
                    Event.current.Use();
                    break;
                case EventType.Repaint:
                    break;
                case EventType.Layout:
                    MousePosition = ev.mousePosition;
                    MousePositionUpdated = true;
                    break;
                case EventType.DragUpdated:
                    break;
                case EventType.DragPerform:
                    break;
                case EventType.DragExited:
                    break;
                case EventType.Ignore:
                    break;
                case EventType.Used:
                    break;
                case EventType.ValidateCommand:
                    break;
                case EventType.ExecuteCommand:
                    break;
                case EventType.ContextClick:
                    break;
                case EventType.MouseEnterWindow:
                    break;
                case EventType.MouseLeaveWindow:
                    break;
                case EventType.TouchDown:
                    break;
                case EventType.TouchUp:
                    break;
                case EventType.TouchMove:
                    break;
                case EventType.TouchEnter:
                    break;
                case EventType.TouchLeave:
                    break;
                case EventType.TouchStationary:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            return this;
        }
    }

    class StateInfo
    {
        public bool IsMouseDown { get; private set; }
        public bool IsMouseUp { get; private set; }
        public bool IsScrollWheel { get; private set; }
        public bool IsKeyDown { get; private set; }
        public bool IsKeyUp { get; private set; }
        public bool IsKey { get; private set; }
        public bool IsMouse { get; private set; }
        public Vector2 MousePosition { get; private set; }

        public Vector2 Scroll { get; private set; }
        public int Button { get; private set; }
        public KeyCode Key { get; private set; }
        public bool ReadyToRender { get; private set; }

        public void Clear()
        {
            IsMouseDown = IsMouseUp = IsMouse = IsKey = IsKeyDown = IsKeyUp = IsScrollWheel = ReadyToRender = false;
            Button = 0;
            Key = (KeyCode) 0;
            Scroll = Vector2.zero;
        }

        public UnityEventInfo Process(int controlID)
        {
            UnityEventInfo eventInfo = new UnityEventInfo().Process(controlID);
            IsMouseDown |= eventInfo.IsMouseDown;
            IsMouseUp |= eventInfo.IsMouseUp;
            IsScrollWheel |= eventInfo.IsScrollWheel;
            IsKeyDown |= eventInfo.IsKeyDown;
            IsKeyUp |= eventInfo.IsKeyUp;
            IsKey |= eventInfo.IsKey;
            IsMouse |= eventInfo.IsMouse;
            if (eventInfo.MousePositionUpdated)
                MousePosition = eventInfo.MousePosition;
            if (eventInfo.IsScrollWheel)
                Scroll = eventInfo.Scroll;
            if (eventInfo.IsMouse)
                Button = eventInfo.Button;
            if (eventInfo.IsKey)
                Key = eventInfo.Key;
            ReadyToRender |= eventInfo.ReadyToRender;
            return eventInfo;
        }

        public override string ToString()
        {
            return
                $"render? {ReadyToRender} mousedown? {IsMouseDown} button? {Button} mousepos: {MousePosition} iskey? {IsKeyDown} key:{Key}";
        }
    }
}
