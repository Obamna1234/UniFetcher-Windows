using Nickvision.Desktop.Globalization;
using System;

namespace Nickvision.Parabolic.Shared.Models;

public class VideoResolution : IComparable<VideoResolution>, IEquatable<VideoResolution>
{
    public static VideoResolution Best { get; }
    public static VideoResolution Worst { get; }
    /// <summary>UniFetcher quality rule (see Format.SmartVideo).</summary>
    public static VideoResolution Smart { get; }

    public int Width { get; }
    public int Height { get; }

    static VideoResolution()
    {
        Best = new VideoResolution(int.MaxValue, int.MaxValue);
        Worst = new VideoResolution(-1, -1);
        Smart = new VideoResolution(-2, -2);
    }

    public VideoResolution() : this(int.MaxValue, int.MaxValue)
    {

    }

    public VideoResolution(int width, int height)
    {
        Width = Math.Max(width, height);
        Height = Math.Min(width, height);
    }

    public static VideoResolution? Parse(string value, ITranslationService translator)
    {
        if (value == "Best" || value == translator._("Best"))
        {
            return Best;
        }
        else if (value == "Worst" || value == translator._("Worst"))
        {
            return Worst;
        }
        else if (value == "Smart: best up to 1080p" || value == translator._("Smart: best up to 1080p"))
        {
            return Smart;
        }
        var parts = value.Split('x');
        if (parts.Length != 2)
        {
            return null;
        }
        if (int.TryParse(parts[0], out int width) && int.TryParse(parts[1], out int height))
        {
            return new VideoResolution(width, height);
        }
        return null;
    }

    public int CompareTo(VideoResolution? other)
    {
        if (other is null)
        {
            return 1;
        }
        var widthCompare = Width.CompareTo(other.Width);
        return widthCompare == 0 ? Height.CompareTo(other.Height) : widthCompare;
    }

    public override bool Equals(object? obj) => obj is VideoResolution other && Equals(other);

    public bool Equals(VideoResolution? other) => other is not null && Width == other.Width && Height == other.Height;

    public override int GetHashCode() => HashCode.Combine(Width, Height);

    public override string ToString() => ToString(null);

    public string ToString(ITranslationService? translator)
    {
        if (Width == int.MaxValue && Height == int.MaxValue)
        {
            return translator?._("Best") ?? "Best";
        }
        else if (Width == -1 && Height == -1)
        {
            return translator?._("Worst") ?? "Worst";
        }
        else if (Width == -2 && Height == -2)
        {
            return translator?._("Smart: best up to 1080p") ?? "Smart: best up to 1080p";
        }
        return $"{Width}x{Height}";
    }

    public static bool operator >(VideoResolution left, VideoResolution right) => left.CompareTo(right) > 0;

    public static bool operator <(VideoResolution left, VideoResolution right) => left.CompareTo(right) < 0;

    public static bool operator >=(VideoResolution left, VideoResolution right) => left.CompareTo(right) >= 0;

    public static bool operator <=(VideoResolution left, VideoResolution right) => left.CompareTo(right) <= 0;

    public static bool operator ==(VideoResolution left, VideoResolution right) => left.Equals(right);

    public static bool operator !=(VideoResolution left, VideoResolution right) => !left.Equals(right);
}
