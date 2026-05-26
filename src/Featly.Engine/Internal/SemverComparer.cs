namespace Featly.Engine.Internal;

/// <summary>
/// Lightweight semver 2.0.0 comparison sufficient for targeting. Supports
/// <c>MAJOR.MINOR.PATCH</c> with optional <c>-prerelease</c> tags and a
/// dropped build-metadata suffix (after <c>+</c>). Numeric segments are
/// compared as integers; pre-release identifiers compare alphanumerically
/// per the spec.
/// </summary>
/// <remarks>
/// We don't take a System.Net.Semver-like dependency; the spec slice we
/// need is small enough that parsing in-house keeps Featly.Engine
/// dependency-free.
/// </remarks>
internal static class SemverComparer
{
    public static bool TryCompare(string left, string right, out int comparison)
    {
        if (!TryParse(left, out var lv) || !TryParse(right, out var rv))
        {
            comparison = 0;
            return false;
        }

        comparison = lv.CompareTo(rv);
        return true;
    }

    private static bool TryParse(string input, out SemverValue value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        // Drop optional build metadata after `+`.
        var plus = input.IndexOf('+', StringComparison.Ordinal);
        var head = plus < 0 ? input : input[..plus];

        // Split prerelease.
        var dash = head.IndexOf('-', StringComparison.Ordinal);
        var core = dash < 0 ? head : head[..dash];
        var pre = dash < 0 ? null : head[(dash + 1)..];

        var parts = core.Split('.');
        if (parts.Length is < 1 or > 3)
        {
            return false;
        }

        if (!int.TryParse(parts[0], out var major) || major < 0)
        { return false; }
        var minor = 0;
        var patch = 0;
        if (parts.Length >= 2 && (!int.TryParse(parts[1], out minor) || minor < 0))
        { return false; }
        if (parts.Length == 3 && (!int.TryParse(parts[2], out patch) || patch < 0))
        { return false; }

        value = new SemverValue(major, minor, patch, pre);
        return true;
    }

    private readonly record struct SemverValue(int Major, int Minor, int Patch, string? PreRelease)
        : IComparable<SemverValue>
    {
        public int CompareTo(SemverValue other)
        {
            var c = Major.CompareTo(other.Major);
            if (c != 0)
            { return c; }
            c = Minor.CompareTo(other.Minor);
            if (c != 0)
            { return c; }
            c = Patch.CompareTo(other.Patch);
            if (c != 0)
            { return c; }

            // Spec: a version without prerelease > version with prerelease.
            if (PreRelease is null && other.PreRelease is null)
            { return 0; }
            if (PreRelease is null)
            { return 1; }
            if (other.PreRelease is null)
            { return -1; }
            return ComparePreRelease(PreRelease, other.PreRelease);
        }

        private static int ComparePreRelease(string a, string b)
        {
            var aParts = a.Split('.');
            var bParts = b.Split('.');
            var len = Math.Min(aParts.Length, bParts.Length);

            for (var i = 0; i < len; i++)
            {
                var aIsNum = int.TryParse(aParts[i], out var aNum);
                var bIsNum = int.TryParse(bParts[i], out var bNum);

                int cmp;
                if (aIsNum && bIsNum)
                {
                    cmp = aNum.CompareTo(bNum);
                }
                else if (aIsNum != bIsNum)
                {
                    // Numeric identifiers always have lower precedence than alphanumeric.
                    cmp = aIsNum ? -1 : 1;
                }
                else
                {
                    cmp = string.CompareOrdinal(aParts[i], bParts[i]);
                }

                if (cmp != 0)
                { return cmp; }
            }

            return aParts.Length.CompareTo(bParts.Length);
        }
    }
}
