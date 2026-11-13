using System;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Fanuc.RobotInterface;

public static partial class IRobotIFExtension
{
    private static readonly HttpClient HttpClient = new();
    private const string KclSuccessRegexPattern = @"<BODY.*>.*<XMP.*>\s*(?<XMP>.*)\s*</\s*XMP>.*</\s*BODY>";
    private const string KclFailRegexPattern = @"<BODY.*>.*ERROR:\s*([^<]*)\s*.*</\s*BODY>";

#if NET7_0_OR_GREATER
    [GeneratedRegex(KclSuccessRegexPattern)]
    private static partial Regex GetKclSuccessRegex();
    [GeneratedRegex(KclFailRegexPattern)]
    private static partial Regex GetKclFailRegex();
#else
    private static readonly Lazy<Regex> CachedKclSuccessRegex = new(() => new Regex(KclSuccessRegexPattern, RegexOptions.Compiled));
    private static readonly Lazy<Regex> CachedKclFailRegex = new(() => new Regex(KclFailRegexPattern, RegexOptions.Compiled));
    private static Regex GetKclSuccessRegex() => CachedKclSuccessRegex.Value;
    private static Regex GetKclFailRegex() => CachedKclFailRegex.Value;
#endif

    public static void ResetAlarm(this IRobotIF robot) => robot.ExecuteKCL("reset");

    public static string ExecuteKCL(this IRobotIF robot, string command)
    {
        using var response = HttpClient.GetAsync($"http://{robot.Host}/KCL/{command}").Result;
        response.EnsureSuccessStatusCode();
        var content = response.Content.ReadAsStringAsync().Result;
        var success = GetKclSuccessRegex().Match(content);
        if (success.Success)
            return success.Groups["XMP"].Value;
        var failure = GetKclFailRegex().Match(content);
        if (failure.Success)
            throw new IOException($"Executing KCL \"{command}\" failed: {failure.Groups[1].Value}");
        throw new IOException($"Executing KCL \"{command}\" failed: {content}");
    }

    public static string ExecuteKarel(this IRobotIF robot, string program)
    {
        using var response = HttpClient.GetAsync($"http://{robot.Host}/KAREL/{program}").Result;
        response.EnsureSuccessStatusCode();
        return response.Content.ReadAsStringAsync().Result;
    }

    public const int OFFSET_SDIO = 0;
    public const int OFFSET_RDIO = 5000;
    public const int OFFSET_UOP = 6000;
    public const int OFFSET_SOP = 7000;
    public const int OFFSET_WIO = 8000;
    public const int OFFSET_WSI = 8400;
    public const int OFFSET_PMC_K = 10000;
    public const int OFFSET_PMC_R = 11000;

    public const int OFFSET_GIO = 0;
    public const int OFFSET_AIO = 1000;
    public const int OFFSET_PMC_D = 10000;

    #region Read
    private static bool[] GuardedReadDI(this IRobotIF robot, int index, ushort count, [CallerMemberName] string caller = nameof(IRobotIF.ReadDI))
    {
        try
        {
            return robot.ReadDI(index, count);
        }
        catch (Exception ex)
        {
            throw new IOException($"{caller}(index: {index}, count: {count}) failed", ex);
        }
    }
    private static bool[] GuardedReadDO(this IRobotIF robot, int index, ushort count, [CallerMemberName] string caller = nameof(IRobotIF.ReadDO))
    {
        try
        {
            return robot.ReadDO(index, count);
        }
        catch (Exception ex)
        {
            throw new IOException($"{caller}(index: {index}, count: {count}) failed", ex);
        }
    }

    public static bool[] ReadSDI(this IRobotIF robot, int index, ushort count) => robot.GuardedReadDI(index + OFFSET_SDIO, count);
    public static bool[] ReadSDO(this IRobotIF robot, int index, ushort count) => robot.GuardedReadDO(index + OFFSET_SDIO, count);
    public static bool[] ReadRDI(this IRobotIF robot, int index, ushort count) => robot.GuardedReadDI(index + OFFSET_RDIO, count);
    public static bool[] ReadRDO(this IRobotIF robot, int index, ushort count) => robot.GuardedReadDO(index + OFFSET_RDIO, count);
    public static bool[] ReadUI(this IRobotIF robot, int index, ushort count) => robot.GuardedReadDI(index + OFFSET_UOP, count);
    public static bool[] ReadUO(this IRobotIF robot, int index, ushort count) => robot.GuardedReadDO(index + OFFSET_UOP, count);
    public static bool[] ReadSI(this IRobotIF robot, int index, ushort count) => robot.GuardedReadDI(index + OFFSET_SOP, count);
    public static bool[] ReadSO(this IRobotIF robot, int index, ushort count) => robot.GuardedReadDO(index + OFFSET_SOP, count);
    public static bool[] ReadWI(this IRobotIF robot, int index, ushort count) => robot.GuardedReadDI(index + OFFSET_WIO, count);
    public static bool[] ReadWSI(this IRobotIF robot, int index, ushort count) => robot.GuardedReadDI(index + OFFSET_WSI, count);
    public static bool[] ReadWO(this IRobotIF robot, int index, ushort count) => robot.GuardedReadDO(index + OFFSET_WIO, count);
    public static bool[] ReadPmcK(this IRobotIF robot, int index, ushort count) => robot.GuardedReadDO(index + OFFSET_PMC_K, count);
    public static bool[] ReadPmcR(this IRobotIF robot, int index, ushort count) => robot.GuardedReadDO(index + OFFSET_PMC_R, count);

    private static ushort[] GuardedReadGI(this IRobotIF robot, int index, ushort count, [CallerMemberName] string caller = nameof(IRobotIF.ReadGI))
    {
        try
        {
            return robot.ReadGI(index, count);
        }
        catch (Exception ex)
        {
            throw new IOException($"{caller}(index: {index}, count: {count}) failed", ex);
        }
    }
    private static ushort[] GuardedReadGO(this IRobotIF robot, int index, ushort count, [CallerMemberName] string caller = nameof(IRobotIF.ReadGO))
    {
        try
        {
            return robot.ReadGO(index, count);
        }
        catch (Exception ex)
        {
            throw new IOException($"{caller}(index: {index}, count: {count}) failed", ex);
        }
    }

    public static ushort[] ReadGI(this IRobotIF robot, int index, ushort count) => robot.GuardedReadGI(index + OFFSET_GIO, count);
    public static ushort[] ReadGO(this IRobotIF robot, int index, ushort count) => robot.GuardedReadGO(index + OFFSET_GIO, count);
    public static ushort[] ReadAI(this IRobotIF robot, int index, ushort count) => robot.GuardedReadGI(index + OFFSET_AIO, count);
    public static ushort[] ReadAO(this IRobotIF robot, int index, ushort count) => robot.GuardedReadGO(index + OFFSET_AIO, count);
    public static ushort[] ReadPmcD(this IRobotIF robot, int index, ushort count) => robot.GuardedReadGO(index + OFFSET_PMC_D, count);
    #endregion

    #region Write
    private static void GuardedWriteDI(this IRobotIF robot, int index, bool[] values, [CallerMemberName] string caller = nameof(IRobotIF.WriteDI))
    {
        try
        {
            robot.WriteDI(index, values);
        }
        catch (Exception ex)
        {
            throw new IOException($"{caller}(index: {index}, values = {{{string.Join(", ", values)}}}) failed", ex);
        }
    }
    private static void GuardedWriteDO(this IRobotIF robot, int index, bool[] values, [CallerMemberName] string caller = nameof(IRobotIF.WriteDO))
    {
        try
        {
            robot.WriteDO(index, values);
        }
        catch (Exception ex)
        {
            throw new IOException($"{caller}(index: {index}, values = {{{string.Join(", ", values)}}}) failed", ex);
        }
    }

    public static void WriteSDI(this IRobotIF robot, int index, params bool[] values) => robot.GuardedWriteDI(index + OFFSET_SDIO, values);
    public static void WriteSDO(this IRobotIF robot, int index, params bool[] values) => robot.GuardedWriteDO(index + OFFSET_SDIO, values);
    public static void WriteRDI(this IRobotIF robot, int index, params bool[] values) => robot.GuardedWriteDI(index + OFFSET_RDIO, values);
    public static void WriteRDO(this IRobotIF robot, int index, params bool[] values) => robot.GuardedWriteDO(index + OFFSET_RDIO, values);
    public static void WriteUI(this IRobotIF robot, int index, params bool[] values) => robot.GuardedWriteDI(index + OFFSET_UOP, values);
    public static void WriteUO(this IRobotIF robot, int index, params bool[] values) => robot.GuardedWriteDO(index + OFFSET_UOP, values);
    public static void WriteSI(this IRobotIF robot, int index, params bool[] values) => robot.GuardedWriteDI(index + OFFSET_SOP, values);
    public static void WriteSO(this IRobotIF robot, int index, params bool[] values) => robot.GuardedWriteDO(index + OFFSET_SOP, values);
    public static void WriteWI(this IRobotIF robot, int index, params bool[] values) => robot.GuardedWriteDI(index + OFFSET_WIO, values);
    public static void WriteWSI(this IRobotIF robot, int index, params bool[] values) => robot.GuardedWriteDI(index + OFFSET_WSI, values);
    public static void WriteWO(this IRobotIF robot, int index, params bool[] values) => robot.GuardedWriteDO(index + OFFSET_WIO, values);
    public static void WritePmcK(this IRobotIF robot, int index, params bool[] values) => robot.GuardedWriteDO(index + OFFSET_PMC_K, values);
    public static void WritePmcR(this IRobotIF robot, int index, params bool[] values) => robot.GuardedWriteDO(index + OFFSET_PMC_R, values);

    private static void GuardedWriteGI(this IRobotIF robot, int index, ushort count, ushort[] values, [CallerMemberName] string caller = nameof(IRobotIF.WriteGI))
    {
        try
        {
            robot.WriteGI(index, values);
        }
        catch (Exception ex)
        {
            throw new IOException($"{caller}(index: {index}, count: {count}) failed", ex);
        }
    }
    private static void GuardedWriteGO(this IRobotIF robot, int index, ushort count, ushort[] values, [CallerMemberName] string caller = nameof(IRobotIF.WriteGO))
    {
        try
        {
            robot.WriteGO(index, values);
        }
        catch (Exception ex)
        {
            throw new IOException($"{caller}(index: {index}, count: {count}) failed", ex);
        }
    }

    public static void WriteGI(this IRobotIF robot, int index, params ushort[] values) => robot.GuardedWriteGI(index + OFFSET_GIO, (ushort)values.Length, values);
    public static void WriteGO(this IRobotIF robot, int index, params ushort[] values) => robot.GuardedWriteGO(index + OFFSET_GIO, (ushort)values.Length, values);
    public static void WriteAI(this IRobotIF robot, int index, params ushort[] values) => robot.GuardedWriteGI(index + OFFSET_AIO, (ushort)values.Length, values);
    public static void WriteAO(this IRobotIF robot, int index, params ushort[] values) => robot.GuardedWriteGO(index + OFFSET_AIO, (ushort)values.Length, values);
    public static void WritePmcD(this IRobotIF robot, int index, params ushort[] values) => robot.GuardedWriteGO(index + OFFSET_PMC_D, (ushort)values.Length, values);
    #endregion
}