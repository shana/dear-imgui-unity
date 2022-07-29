using System;
using System.Runtime.InteropServices;
using System.Text;
using Unity.Collections.LowLevel.Unsafe;

namespace ImGuiNET
{
    internal static unsafe class Util
    {
        internal const int StackAllocationSizeLimit = 2048;

        public static string StringFromPtr(byte* ptr)
        {
            int characters = 0;
            while (ptr[characters] != 0)
            {
                characters++;
            }

            return Encoding.UTF8.GetString(ptr, characters);
        }

        internal static bool AreStringsEqual(byte* a, int aLength, byte* b)
        {
            for (int i = 0; i < aLength; i++)
            {
                if (a[i] != b[i]) { return false; }
            }

            if (b[aLength] != 0) { return false; }

            return true;
        }

        internal static byte* Allocate(int byteCount) => (byte*)Marshal.AllocHGlobal(byteCount);
        internal static void Free(byte* ptr) => Marshal.FreeHGlobal((IntPtr)ptr);
        internal static int GetUtf8(string s, byte* utf8Bytes, int utf8ByteCount)
        {
            fixed (char* utf16Ptr = s)
            {
                return Encoding.UTF8.GetBytes(utf16Ptr, s.Length, utf8Bytes, utf8ByteCount);
            }
        }
    }

    internal unsafe static class Unsafe
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
}
