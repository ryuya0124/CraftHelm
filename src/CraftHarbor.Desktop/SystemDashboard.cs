using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CraftHarbor.Core;

namespace CraftHarbor.Desktop;

public static class SystemDashboard
{
    public static void Build(Panel parent)
    {
        parent.Children.Add(new TextBlock { Text = "このPCの状態", FontSize = 22, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 12) });
        var tiles = new WrapPanel(); parent.Children.Add(tiles);
        Tile(tiles, "CPU", Environment.ProcessorCount.ToString(), "論理コア", "◈");
        var memory = new MemoryStatus { Length = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MemoryStatus>() };
        if (!GlobalMemoryStatusEx(ref memory)) throw new System.ComponentModel.Win32Exception();
        var totalRam = memory.TotalPhysical;
        var freeRam = memory.AvailablePhysical;
        Tile(tiles, "メモリ", $"{(totalRam - freeRam) / 1073741824d:F1} / {totalRam / 1073741824d:F1} GB", "使用中 / 搭載量", "▤");
        using (var process = Process.GetCurrentProcess()) Tile(tiles, "CraftHelm", $"{process.WorkingSet64 / 1048576d:F0} MB", "アプリの使用メモリ", "◇");
        Tile(tiles, "OS", RuntimeInformation.OSArchitecture.ToString(), RuntimeInformation.OSDescription, "▣");
        Meter(parent, "物理メモリ", (long)(totalRam - freeRam), (long)totalRam, "使用中");
        var drives = DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed).ToArray();
        foreach (var drive in drives) Meter(parent, "ドライブ " + drive.Name, drive.TotalSize - drive.AvailableFreeSpace, drive.TotalSize, "使用中");
        var connections = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
        connections.Children.Add(new TextBlock { Text = "ネットワーク", FontSize = 18, FontWeight = FontWeights.SemiBold });
        var addresses = NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork).Select(a => $"{n.Name}  •  {a.Address}"))
            .Take(8).ToArray();
        foreach (var address in addresses) connections.Children.Add(new TextBlock { Text = address, Margin = new Thickness(0, 6, 0, 0), Foreground = Theme.Brush("Label") });
        if (addresses.Length == 0) connections.Children.Add(new TextBlock { Text = "IPv4アドレスはありません。", Foreground = Theme.Brush("Muted") });
        parent.Children.Add(Surface(connections));
        var raw = new TextBox { Text = Diagnostics.Describe(), IsReadOnly = true, AcceptsReturn = true, MinHeight = 180, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        parent.Children.Add(new Expander { Header = "詳細情報と待ち受けポート", Content = raw, Margin = new Thickness(0, 16, 0, 0), Foreground = Theme.Brush("Ink") });
    }

    private static void Tile(Panel parent, string label, string value, string detail, string symbol)
    {
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = symbol + "  " + label, Foreground = Theme.Brush("Accent"), FontWeight = FontWeights.SemiBold });
        content.Children.Add(new TextBlock { Text = value, FontSize = 21, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 7, 0, 4) });
        content.Children.Add(new TextBlock { Text = detail, FontSize = 11, Foreground = Theme.Brush("Muted") });
        var card = Surface(content); card.Width = 205; card.MinHeight = 115; card.Margin = new Thickness(0, 0, 10, 10); parent.Children.Add(card);
    }

    private static void Meter(Panel parent, string label, long used, long total, string unit)
    {
        var content = new StackPanel();
        var percentage = total == 0 ? 0 : Math.Clamp(used * 100d / total, 0, 100);
        content.Children.Add(new TextBlock { Text = $"{label}    {percentage:F0}% {unit}", FontWeight = FontWeights.SemiBold });
        content.Children.Add(new ProgressBar { Name = "SystemMeter", Value = percentage, Maximum = 100, Height = 12, Margin = new Thickness(0, 12, 0, 8), Foreground = Theme.Brush("Accent"), Background = Theme.Brush("Input") });
        content.Children.Add(new TextBlock { Text = $"{used / 1073741824d:F1} / {total / 1073741824d:F1} GB", Foreground = Theme.Brush("Muted"), FontSize = 12 });
        parent.Children.Add(Surface(content));
    }

    private static Border Surface(UIElement content) => new()
    {
        Background = Theme.Brush("Surface"), CornerRadius = new CornerRadius(10), Padding = new Thickness(18),
        Margin = new Thickness(0, 0, 0, 10), Child = content
    };

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
}
