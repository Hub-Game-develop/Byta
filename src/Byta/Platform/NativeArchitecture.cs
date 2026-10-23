using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Byta.Platform;

/// <summary>
/// 判断一个 Mach-O / PE / ELF 原生库的架构是否和当前进程兼容。
/// <para>
/// 为什么需要它：苹果 Silicon 上经常出现 x64 进程（Rosetta）遇到 arm64 原生库的情况，
/// 直接 <c>dlopen</c> 会抛 <see cref="BadImageFormatException"/>。与其崩，不如提前识别、
/// 明确告诉调用方"这个实现用不了，原因是什么"。
/// </para>
/// </summary>
internal static class NativeArchitecture
{
    private const uint FatMagicBigEndian = 0xCAFEBABE;
    private const uint FatMagicLittleEndian = 0xBEBAFECA;
    private const uint MachMagic64 = 0xFEEDFACF;
    private const uint MachMagic32 = 0xFEEDFACE;
    private const uint CpuTypeX64 = 0x01000007;
    private const uint CpuTypeArm64 = 0x0100000C;
    private const uint CpuTypeX86 = 0x00000007;
    private const uint CpuTypeArm = 0x0000000C;

    /// <summary>当前进程对应的 CPU 类型；非 Mach-O 平台返回 0（表示"不检查"）。</summary>
    public static uint CurrentProcessCpuType => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X64 => CpuTypeX64,
        Architecture.Arm64 => CpuTypeArm64,
        Architecture.X86 => CpuTypeX86,
        Architecture.Arm => CpuTypeArm,
        _ => 0,
    };

    /// <summary>
    /// 检查原生库能否被当前进程加载。能加载或无法判断时返回 true；
    /// 确定架构不匹配时返回 false，并通过 <paramref name="reason"/> 说明原因。
    /// </summary>
    public static bool IsCompatible(string path, out string? reason)
    {
        reason = null;

        if (!OperatingSystem.IsMacOS())
            return true; // 其他平台的结构判断交给运行时，我们只处理当前遇到的这一类问题

        uint wanted = CurrentProcessCpuType;
        if (wanted == 0)
            return true;

        try
        {
            Span<byte> header = stackalloc byte[4096];
            using var stream = File.OpenRead(path);
            int read = stream.Read(header);
            if (read < 8)
                return true;

            // Mach-O 的文件头是小端写入的，所以要两种字节序都读一遍才能正确识别
            uint magicBigEndian = BinaryPrimitives.ReadUInt32BigEndian(header);
            uint magicLittleEndian = BinaryPrimitives.ReadUInt32LittleEndian(header);

            // 通用二进制（fat）
            if (magicBigEndian == FatMagicBigEndian || magicLittleEndian == FatMagicLittleEndian)
            {
                bool bigEndian = magicBigEndian == FatMagicBigEndian;
                uint count = bigEndian
                    ? BinaryPrimitives.ReadUInt32BigEndian(header[4..])
                    : BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);

                for (int i = 0; i < count; i++)
                {
                    int offset = 8 + i * 20; // fat_arch: cputype, cpusubtype, offset, size, align
                    if (offset + 4 > read)
                        break;
                    uint cpuType = bigEndian
                        ? BinaryPrimitives.ReadUInt32BigEndian(header[offset..])
                        : BinaryPrimitives.ReadUInt32LittleEndian(header[offset..]);
                    if (cpuType == wanted)
                        return true;
                }

                reason = "通用二进制里没有当前进程架构的分片";
                return false;
            }

            // 单架构 Mach-O（0xFEEDFACF / 0xFEEDFACE 是文件里的实际字节序，即小端）
            if (magicLittleEndian is MachMagic64 or MachMagic32)
            {
                uint cpuType = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
                if (cpuType == wanted)
                    return true;

                reason = $"架构不匹配：库是 {Describe(cpuType)}，当前进程是 {Describe(wanted)}";
                return false;
            }

            return true; // 不是 Mach-O，交给运行时判断
        }
        catch (IOException)
        {
            return true;
        }
    }

    private static string Describe(uint cpuType) => cpuType switch
    {
        CpuTypeX64 => "x86_64",
        CpuTypeArm64 => "arm64",
        CpuTypeX86 => "i386",
        CpuTypeArm => "arm",
        _ => $"cputype=0x{cpuType:X}",
    };
}
