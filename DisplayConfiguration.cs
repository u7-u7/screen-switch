using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;

internal static class DisplayConfiguration
{
    internal const uint Clone = 2, Extend = 4;
    private const uint Validate = 0x40;
    private const uint Apply = 0x80;
    private const uint SuppliedConfiguration = 0x20;
    private const uint SaveToDatabase = 0x200;
    private const uint AllowChanges = 0x400;
    private const uint InvalidMode = uint.MaxValue;

    [StructLayout(LayoutKind.Sequential)]
    internal struct AdapterId
    {
        internal uint Low;
        internal int High;
    }

    internal struct Endpoint : IEquatable<Endpoint>
    {
        internal AdapterId Adapter;
        internal uint Id;

        internal Endpoint(AdapterId adapter, uint id)
        {
            Adapter = adapter;
            Id = id;
        }

        public bool Equals(Endpoint other)
        {
            return Id == other.Id && Adapter.Low == other.Adapter.Low && Adapter.High == other.Adapter.High;
        }

        public override bool Equals(object obj)
        {
            return obj is Endpoint && Equals((Endpoint)obj);
        }

        public override int GetHashCode()
        {
            return unchecked((int)Id * 397 ^ (int)Adapter.Low ^ Adapter.High);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Source
    {
        internal AdapterId Adapter;
        internal uint Id, ModeIndex, Status;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Target
    {
        internal AdapterId Adapter;
        internal uint Id, ModeIndex, Technology, Rotation, Scaling;
        internal uint RefreshNumerator, RefreshDenominator, ScanLineOrdering;
        internal int Available;
        internal uint Status;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Path
    {
        internal Source Source;
        internal Target Target;
        internal uint Flags;

        internal Endpoint SourceKey { get { return new Endpoint(Source.Adapter, Source.Id); } }
        internal Endpoint TargetKey { get { return new Endpoint(Target.Adapter, Target.Id); } }

        internal bool BuiltIn
        {
            get
            {
                return Target.Technology == 0x80000000 || Target.Technology == 6
                    || Target.Technology == 11 || Target.Technology == 13;
            }
        }

        internal string ScreenName
        {
            get
            {
                var packet = new SourceName
                {
                    Type = 1,
                    Size = (uint)Marshal.SizeOf(typeof(SourceName)),
                    Adapter = Source.Adapter,
                    Id = Source.Id
                };
                return DisplayConfigGetDeviceInfo(ref packet) == 0 ? packet.Name : null;
            }
        }

        internal string FriendlyName
        {
            get
            {
                var name = ReadTargetName().Name;
                return string.IsNullOrWhiteSpace(name) ? (BuiltIn ? "笔记本屏幕" : "外接屏幕") : name;
            }
        }

        internal string DevicePath { get { return ReadTargetName().DevicePath; } }

        private TargetName ReadTargetName()
        {
            var packet = new TargetName
            {
                Type = 2,
                Size = (uint)Marshal.SizeOf(typeof(TargetName)),
                Adapter = Target.Adapter,
                Id = Target.Id
            };
            return DisplayConfigGetDeviceInfo(ref packet) == 0 ? packet : default(TargetName);
        }
    }

    [StructLayout(LayoutKind.Explicit, Size = 64)]
    internal struct Mode
    {
        // The source mode's desktop origin is relative to the primary display.
        [FieldOffset(16)] internal uint Width;
        [FieldOffset(20)] internal uint Height;
        [FieldOffset(28)] internal int X;
        [FieldOffset(32)] internal int Y;
        [FieldOffset(16)] private long nativeUnionAlignment;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SourceName
    {
        internal uint Type, Size;
        internal AdapterId Adapter;
        internal uint Id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        internal string Name;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TargetName
    {
        internal uint Type, Size;
        internal AdapterId Adapter;
        internal uint Id;
        internal uint Flags, Technology;
        internal ushort Manufacturer, Product;
        internal uint Connector;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        internal string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        internal string DevicePath;
    }

    internal sealed class Layout
    {
        internal Path[] Paths;
        internal Mode[] Modes;

        internal bool ShowsSelection(Endpoint[] selection)
        {
            return Paths.Length == selection.Length && new HashSet<Endpoint>(selection)
                .SetEquals(Paths.Select(path => path.TargetKey)) && (selection.Length == 1 || Topology == Extend);
        }

        internal uint Topology
        {
            get
            {
                if (Paths.Length < 2)
                    return 0;
                int distinctSources = Paths.Select(path => path.SourceKey).Distinct().Count();
                if (distinctSources > 1 && Paths.All(path => path.Source.ModeIndex < (uint)Modes.Length))
                    distinctSources = Paths.Select(path => Modes[path.Source.ModeIndex])
                        .Select(mode => Tuple.Create(mode.X, mode.Y)).Distinct().Count();
                if (distinctSources == Paths.Length)
                    return Extend;
                return distinctSources == 1 ? Clone : 0;
            }
        }
    }

    internal static Layout Read(bool includeInactive)
    {
        const int insufficientBuffer = 122;
        uint flags = includeInactive ? 1u : 2u;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            uint pathCount, modeCount;
            Check(GetDisplayConfigBufferSizes(flags, out pathCount, out modeCount));
            var paths = new Path[pathCount];
            var modes = new Mode[modeCount];
            var result = QueryDisplayConfig(flags, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
            if (result == insufficientBuffer)
                continue;
            Check(result);
            Array.Resize(ref paths, (int)pathCount);
            Array.Resize(ref modes, (int)modeCount);
            return new Layout { Paths = paths, Modes = modes };
        }
        throw new Win32Exception(insufficientBuffer);
    }

    internal static void SetTopology(uint topology)
    {
        Check(SetDisplayConfig(0, null, 0, null, Validate | topology));
        Check(SetDisplayConfig(0, null, 0, null, Apply | topology));
    }

    internal static void SetLayout(Layout layout, bool save)
    {
        Check(TrySetLayout(layout, save ? SaveToDatabase : 0));
    }

    private static int TrySetLayout(Layout layout, uint options)
    {
        var pathCount = (uint)layout.Paths.Length;
        var modeCount = (uint)layout.Modes.Length;
        var modes = modeCount == 0 ? null : layout.Modes;
        uint flags = SuppliedConfiguration | options;
        var result = SetDisplayConfig(pathCount, layout.Paths, modeCount, modes, Validate | (flags & ~SaveToDatabase));
        return result == 0 ? SetDisplayConfig(pathCount, layout.Paths, modeCount, modes, Apply | flags) : result;
    }

    internal static void ShowSelection(Layout active, Endpoint[] selection)
    {
        if (selection.Length == 0)
            throw new InvalidOperationException("请至少选择一块屏幕。");
        if (selection.Length == 1)
        {
            ShowOnly(active, selection[0]);
            return;
        }

        var connected = Read(true).Paths.Where(path => path.Target.Available != 0).ToArray();
        var candidates = SelectionCandidates(active, connected, selection);
        if (candidates.Any(paths => paths.Length == 0))
            throw new InvalidOperationException("有屏幕已断开，请重新选择。");

        // Try a small number of source assignments if a driver rejects the preferred routes.
        int result = 1610; // ERROR_BAD_CONFIGURATION
        var search = new PathSearch(candidates);
        foreach (var paths in search.Assignments(0))
        {
            var preferred = PlanExtension(active, paths, true);
            if (preferred.Modes.Length > 0)
            {
                result = TrySetLayout(preferred, preferred.Modes.Length == paths.Length * 2 ? 0 : AllowChanges);
                if (result == 0)
                    return;
            }
            result = TrySetLayout(PlanExtension(active, paths, false), AllowChanges);
            if (result == 0)
                return;
        }
        Check(result);
    }

    internal static Path[][] SelectionCandidates(Layout active, Path[] connected, Endpoint[] selection)
    {
        return selection.Select(key => active.Paths.Concat(connected)
            .Where(path => path.TargetKey.Equals(key))
            .GroupBy(path => path.SourceKey).Select(group => group.First()).ToArray()).ToArray();
    }

    internal sealed class PathSearch
    {
        private readonly Path[][] candidates;
        private readonly int[] order;
        private readonly Path[] picked;
        private readonly HashSet<Endpoint> used = new HashSet<Endpoint>();
        private int remaining = 192;

        internal PathSearch(Path[][] candidates)
        {
            this.candidates = candidates;
            order = Enumerable.Range(0, candidates.Length).OrderBy(index => candidates[index].Length).ToArray();
            picked = new Path[candidates.Length];
        }

        internal IEnumerable<Path[]> Assignments(int depth)
        {
            if (remaining-- <= 0)
                yield break;
            if (depth == order.Length)
            {
                yield return (Path[])picked.Clone();
                yield break;
            }
            int index = order[depth];
            foreach (var path in candidates[index])
            {
                if (!used.Add(path.SourceKey))
                    continue;
                picked[index] = path;
                foreach (var assignment in Assignments(depth + 1))
                    yield return assignment;
                used.Remove(path.SourceKey);
            }
        }
    }

    internal static Layout PlanExtension(Layout active, Path[] routes, bool keepModes)
    {
        var origin = active.Paths.Where(path => path.Source.ModeIndex < (uint)active.Modes.Length)
            .OrderBy(path => {
                var mode = active.Modes[path.Source.ModeIndex];
                return mode.X == 0 && mode.Y == 0 ? 0 : 1;
            }).FirstOrDefault(path => routes.Any(route => route.TargetKey.Equals(path.TargetKey)));
        var modes = new List<Mode>();
        var paths = routes.OrderByDescending(route => route.TargetKey.Equals(origin.TargetKey)).ToArray();
        var originalByTarget = active.Paths.ToDictionary(path => path.TargetKey);
        var origins = new Dictionary<Endpoint, Mode>();
        if (keepModes)
        {
            foreach (var path in paths)
            {
                Path original;
                if (originalByTarget.TryGetValue(path.TargetKey, out original) && path.SourceKey.Equals(original.SourceKey)
                    && original.Source.ModeIndex < (uint)active.Modes.Length &&
                    original.Target.ModeIndex < (uint)active.Modes.Length)
                    origins.Add(path.TargetKey, active.Modes[original.Source.ModeIndex]);
            }
            ArrangeSources(origins, paths);
        }

        for (int index = 0; index < paths.Length; index++)
        {
            var path = paths[index];
            Path original;
            bool wasActive = originalByTarget.TryGetValue(path.TargetKey, out original);
            path.Flags = 1;
            path.Source.ModeIndex = path.Target.ModeIndex = InvalidMode;
            if (wasActive)
            {
                path.Target.Rotation = original.Target.Rotation;
                path.Target.Scaling = original.Target.Scaling;
            }
            Mode source;
            if (origins.TryGetValue(path.TargetKey, out source))
            {
                path.Source.ModeIndex = (uint)modes.Count;
                modes.Add(source);
                path.Target.ModeIndex = (uint)modes.Count;
                modes.Add(active.Modes[original.Target.ModeIndex]);
            }
            else
            {
                path.Target.RefreshNumerator = path.Target.RefreshDenominator = path.Target.ScanLineOrdering = 0;
            }
            paths[index] = path;
        }
        return new Layout { Modes = modes.ToArray(), Paths = paths };
    }

    private static void ArrangeSources(Dictionary<Endpoint, Mode> modes, Path[] paths)
    {
        if (modes.Count == 0)
            return;
        var areas = paths.Where(path => modes.ContainsKey(path.TargetKey))
            .ToDictionary(path => path.TargetKey, path => DesktopArea(path, modes[path.TargetKey]));
        var keys = paths.Select(path => path.TargetKey).Where(areas.ContainsKey).ToArray();
        var primary = modes[keys[0]];
        if (CanKeepPositions(keys.Select(key => areas[key]).ToArray()))
        {
            foreach (var key in keys)
            {
                var mode = modes[key];
                mode.X -= primary.X;
                mode.Y -= primary.Y;
                modes[key] = mode;
            }
            return;
        }

        // Collapse gaps left behind by a disabled display, retaining the relative order.
        bool vertical = keys.Select(key => modes[key].X).Distinct().Count() == 1;
        var ordered = keys.OrderBy(key => vertical ? modes[key].Y : modes[key].X).ToArray();
        int offset = -ordered.TakeWhile(key => !key.Equals(keys[0]))
            .Sum(key => vertical ? areas[key].Height : areas[key].Width);
        foreach (var key in ordered)
        {
            var mode = modes[key];
            mode.X = vertical ? 0 : offset;
            mode.Y = vertical ? offset : 0;
            offset += vertical ? areas[key].Height : areas[key].Width;
            modes[key] = mode;
        }
    }

    internal static Dictionary<Endpoint, Rectangle> ExtendedAreas(Layout layout)
    {
        var areas = new Dictionary<Endpoint, Rectangle>();
        if (layout == null || layout.Topology != Extend)
            return areas;
        foreach (var path in layout.Paths)
        {
            if (path.Source.ModeIndex >= (uint)layout.Modes.Length)
                return new Dictionary<Endpoint, Rectangle>();
            areas.Add(path.TargetKey, DesktopArea(path, layout.Modes[path.Source.ModeIndex]));
        }
        return areas;
    }

    private static Rectangle DesktopArea(Path path, Mode mode)
    {
        bool portrait = path.Target.Rotation == 2 || path.Target.Rotation == 4;
        return new Rectangle(mode.X, mode.Y, (int)(portrait ? mode.Height : mode.Width),
            (int)(portrait ? mode.Width : mode.Height));
    }

    internal static Layout MoveScreens(Layout active, Dictionary<Endpoint, Rectangle> original,
        Dictionary<Endpoint, Point> positions)
    {
        var current = ExtendedAreas(active);
        Rectangle startingArea;
        if (current.Count != original.Count || current.Count != positions.Count || current.Any(item =>
            !original.TryGetValue(item.Key, out startingArea) || startingArea != item.Value || !positions.ContainsKey(item.Key)))
            throw new InvalidOperationException("显示配置已变化，请重新排列。");
        if (!CanKeepPositions(current.Select(item => new Rectangle(positions[item.Key], item.Value.Size)).ToArray()))
            throw new InvalidOperationException("让屏幕边缘相接后再应用。");

        var modes = (Mode[])active.Modes.Clone();
        foreach (var path in active.Paths)
        {
            var point = positions[path.TargetKey];
            modes[path.Source.ModeIndex].X = point.X;
            modes[path.Source.ModeIndex].Y = point.Y;
        }
        return new Layout { Paths = active.Paths, Modes = modes };
    }

    internal static bool HasPositions(Layout layout, Dictionary<Endpoint, Point> expected)
    {
        var areas = ExtendedAreas(layout);
        Point position;
        return areas.Count == expected.Count && areas.All(item => expected.TryGetValue(item.Key, out position)
            && position == item.Value.Location);
    }

    internal static bool CanKeepPositions(Rectangle[] rects)
    {
        if (rects.Length == 0)
            return false;
        for (int index = 0; index < rects.Length; index++)
            if (rects.Take(index).Any(rect => rect.IntersectsWith(rects[index])))
                return false;

        var connected = new HashSet<int> { 0 };
        var pending = new Queue<int>();
        pending.Enqueue(0);
        while (pending.Count > 0)
        {
            var first = rects[pending.Dequeue()];
            for (int index = 0; index < rects.Length; index++)
            {
                var second = rects[index];
                bool horizontal = (first.Right == second.Left || second.Right == first.Left)
                    && Math.Min(first.Bottom, second.Bottom) > Math.Max(first.Top, second.Top);
                bool vertical = (first.Bottom == second.Top || second.Bottom == first.Top)
                    && Math.Min(first.Right, second.Right) > Math.Max(first.Left, second.Left);
                if ((horizontal || vertical) && connected.Add(index))
                    pending.Enqueue(index);
            }
        }
        return connected.Count == rects.Length;
    }

    internal static void ShowOnly(Layout layout, Endpoint selected)
    {
        var current = layout.Paths.FirstOrDefault(path => path.TargetKey.Equals(selected));
        if ((current.Flags & 1) != 0 && current.Source.ModeIndex < (uint)layout.Modes.Length
                                    && current.Target.ModeIndex < (uint)layout.Modes.Length)
        {
            var source = layout.Modes[current.Source.ModeIndex];
            var target = layout.Modes[current.Target.ModeIndex];
            source.X = source.Y = 0;
            current.Source.ModeIndex = 0;
            current.Target.ModeIndex = 1;
            var paths = new[] { current };
            var modes = new[] { source, target };
            if (SetDisplayConfig(1, paths, 2, modes, Validate | SuppliedConfiguration) == 0
                && SetDisplayConfig(1, paths, 2, modes, Apply | SuppliedConfiguration) == 0)
                return;
        }

        // Let Windows choose timings when the active timing cannot be used in single-screen mode.
        var candidates = Read(true).Paths.Where(path => path.Target.Available != 0 && path.TargetKey.Equals(selected))
            .OrderByDescending(path => path.SourceKey.Equals(current.SourceKey));
        int failure = 1167; // ERROR_DEVICE_NOT_CONNECTED
        foreach (var candidate in candidates)
        {
            var path = candidate;
            path.Flags = 1;
            path.Source.ModeIndex = path.Target.ModeIndex = InvalidMode;
            path.Target.RefreshNumerator = path.Target.RefreshDenominator = path.Target.ScanLineOrdering = 0;
            if ((current.Flags & 1) != 0)
            {
                path.Target.Rotation = current.Target.Rotation;
                path.Target.Scaling = current.Target.Scaling;
            }
            var paths = new[] { path };
            failure = SetDisplayConfig(1, paths, 0, null, Validate | SuppliedConfiguration);
            if (failure != 0)
                continue;
            Check(SetDisplayConfig(1, paths, 0, null, Apply | SuppliedConfiguration));
            return;
        }
        Check(failure);
    }

    private static void Check(int result)
    {
        if (result != 0)
            throw new Win32Exception(result);
    }

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags, ref uint pathCount, [Out] Path[] paths,
        ref uint modeCount, [Out] Mode[] modes, IntPtr topology);

    [DllImport("user32.dll")]
    private static extern int SetDisplayConfig(uint pathCount, [In] Path[] paths,
        uint modeCount, [In] Mode[] modes, uint flags);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref SourceName packet);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref TargetName packet);
}
