using Unity.Collections.LowLevel.Unsafe;

internal static unsafe class Unsafe
{
	public static ref T AsRef<T>(void* ptr) where T : struct
	{
		return ref UnsafeUtility.AsRef<T>(ptr);
	}

	public static void InitBlockUnaligned(void* startAddress, byte value, uint byteCount)
	{
		var allocator = new UnsafeScratchAllocator(startAddress, (int)byteCount);
		allocator.Allocate((int)byteCount, 0);
	}

	public static void CopyBlock(void* destination, void* source, uint byteCount)
	{
		UnsafeUtility.MemCpy(destination, source, byteCount);
	}

	public static int SizeOf<T>() where T : struct
	{
		return UnsafeUtility.SizeOf<T>();
	}

	public static void* AsPointer<T>(ref T t) where T : struct => UnsafeUtility.AddressOf(ref t);

	public static T Read<T>(void* source) where T : struct
	{
		return UnsafeUtility.ReadArrayElement<T>(source, 0);
	}
}