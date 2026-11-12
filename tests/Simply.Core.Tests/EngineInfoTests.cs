using System.Runtime.InteropServices;

namespace Simply.Core.Tests;

/// <summary>
/// Smoke tests verifying the core library metadata and unmanaged memory configuration.
/// </summary>
public sealed class EngineInfoTests
{
    [Fact]
    public void Name_ReturnsSimply()
    {
        Assert.Equal("Simply", EngineInfo.Name);
    }

    [Fact]
    public unsafe void AlignedAlloc_Allocates64ByteAlignedPointer()
    {
        void* ptr = NativeMemory.AlignedAlloc(64, 64);
        try
        {
            Assert.NotEqual(0, (nint)ptr);
            Assert.Equal(0UL, (nuint)ptr % 64);
        }
        finally
        {
            NativeMemory.AlignedFree(ptr);
        }
    }
}
