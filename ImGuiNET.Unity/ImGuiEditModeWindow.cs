using System;
using ImGuiNET.Unity;
using UnityEngine;

[ExecuteInEditMode]
public class ImGuiEditModeWindow : DearImGui
{
	private static ImGuiEditModeWindow instance;
	private static bool ensureCalledEarly;
	private static event Action readyCallbacks;
	public static bool IsInitialized => instance != null;
	public static bool IsEnabled => IsInitialized && instance.enabled;

	private bool IsPrimary => instance == this;

	protected override void Awake()
	{
		if (!IsInitialized)
			instance = this;
		base.Awake();
	}

	protected override void OnDestroy()
	{
		if (IsPrimary)
			instance = null;
		base.OnDestroy();
	}

	protected override void OnEnable()
	{
		if (!IsInitialized)
			instance = this;

		// this might disable the mb if something fails to initialize
		base.OnEnable();

		if (!enabled)
			return;

		// if the static Ensure() method was called, Activate now
		if (IsPrimary && ensureCalledEarly)
		{
			Activate();
		}
	}

	/// <summary>
	/// Always call this or the <see cref="Activate"/> method before any ImGui calls, and only continue if it returns true.
	/// If you're calling this too early but you really want to issue ImGui calls at that point, use the onReady delegate
	/// to issue the calls, like: `Ensure(() => ImGui.Text("This is a good spot"));`
	///
	/// If you call this with a delegate, but you forget to have the <see cref="ImGuiEditModeWindow"/> in the scene and/or
	/// never enable it, you will leak every class that calls this, they will never get garbage collected, so BE CAREFUL.
	/// Check the <see cref="IsEnabled"/> static property to see if there's a window that you can use.
	/// 
	/// If you call this with a delegate multiple times before the <see cref="ImGuiEditModeWindow"/> gets enabled,
	/// all the delegates will get invoked at the same time once the window gets enabled. If you don't want that, check the
	/// <see cref="IsInitialized"/> property to see if your delegate is going to get deferred.
	/// </summary>
	/// <param name="onReady">Callback that happens when it's safe to issue ImGui calls for this frame, if you prefer
	/// doing it on a callback instead of checking the return value.</param>
	/// <returns>true if it's safe to issue ImGui calls, false if it isn't</returns>
	public static bool Ensure(Action onReady = null)
	{
		if (!IsInitialized && onReady != null)
		{
			readyCallbacks += onReady;
			ensureCalledEarly = true;
			return false;
		}

		// if this is called before Awake or OnEnable is called or after OnDestroy happens, it will throw
		// and prevent subsequent native ImGui calls from happening and potentially crashing
		return instance.Activate(onReady);
	}

	/// <summary>
	/// Always call this or the <see cref="Ensure"/> method before any ImGui calls, and only continue if it returns true.
	/// If you're calling this too early (like, calling this on Awake when the <see cref="ImGuiEditModeWindow"/> isn't ready yet,
	/// use the <see cref="Ensure"/> static method with an action delegate instead, so your ImGui calls can be deferred to when
	/// the window is ready.
	/// </summary>
	/// <returns>true if it's safe to issue ImGui calls, false if it isn't</returns>
	public bool Activate(Action onReady = null)
	{
		EnsureFrame();

		// if the frame isn't ready, bail
		if (!FrameReady)
			return false;

		if (IsPrimary && ensureCalledEarly)
		{
			readyCallbacks();
			readyCallbacks = null;
			ensureCalledEarly = false;
		}

		if (onReady != null)
			onReady();

		return true;
	}
}