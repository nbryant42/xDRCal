using System;

namespace xDRCal;

/// <summary>
/// An EOTF subclass provides the forward and reverse mapping from linear light space to encoded display signal
/// (EOTF and EOTF⁻¹).
/// 
/// In this application, they primarily define the interpretation of the sliders, which range from [0..1023].
/// For that reason, the return value of ToCode, and the parameter of ToLinear are not scaled in the usual
/// manner, because 1.0 is not defined in the same way for each of our usages. For example, for PQ, E' is defined over
/// [0..1] and we map this to [0..1023] as per the standard 10 bit integer encoding, but for sRGB and Gamma 2.2, it is
/// more natural to map 1.0 to the 255 point and use the [256..1023] range as an HDR extension.
/// 
/// For slider purposes, these methods are conceptualized around 10-bit values scaled to 0..1023, but accept and return
/// floating point to support higher precision gradients on 12-bit displays. Any subsequent integer conversions should
/// be via MathF.Round().
/// </summary>
public abstract class EOTF
{
    protected EOTF(string displayName)
    {
        DisplayName = displayName;
    }

    /// <summary>
    /// Convert codepoint to linear nits
    /// </summary>
    /// <param name="signal">Slider value in range [0..1023]</param>
    /// <returns>Luminance in nits.</returns>
    public abstract float ToNits(float signal);
    public float ToScRGB(float signal) => ToNits(signal) * 0.0125f; // 1/80 nits

    /// <summary>
    /// Convert linear nits to codepoint
    /// </summary>
    /// <param name="nits">Luminance in nits.</param>
    /// <returns>Slider value in range [0..1023]</returns>
    public abstract float ToCode(float nits);

    public static readonly EOTF pq = new PQ();
    public static readonly EOTF sRGB = new SRGB();
    public static readonly EOTF gamma22 = new Gamma22();
    public static readonly EOTF gamma24 = new Gamma24();

    public string DisplayName { get; private set; }

    private class PQ : EOTF
    {
        public PQ() : base("PQ")
        {
        }

        public override float ToCode(float nits)
        {
            var Ym1 = MathF.Pow(nits / 10000.0f, 1305.0f / 8192.0f);

            var numerator = (107.0f / 128.0f) + (2413.0f / 128.0f) * Ym1;
            var denominator = 1.0f + (2392.0f / 128.0f) * Ym1;

            return MathF.Pow(numerator / denominator, 2523.0f / 32.0f) * 1023.0f;
        }

        public override float ToNits(float signal)
        {
            float Nn_pow = MathF.Pow(signal / 1023.0f, 32.0f / 2523.0f);

            float numerator = Math.Max(Nn_pow - 107.0f / 128.0f, 0.0f);
            float denominator = 2413.0f / 128.0f - 2392.0f / 128.0f * Nn_pow;

            return MathF.Pow(numerator / denominator, 8192.0f / 1305.0f) * 10000.0f;
        }
    }

    private class SRGB : EOTF
    {
        public SRGB() : base("sRGB (extended)")
        {
        }

        public override float ToCode(float nits)
        {
            var R = nits * 0.0125f;
            return (R <= 0.0031308f ? 12.92f * R : 1.055f * MathF.Pow(R, 1.0f/2.4f) - 0.055f) * 255.0f;
        }

        public override float ToNits(float signal)
        {
            var Rprime = signal / 255.0f;
            return (Rprime <= 0.04045f ? Rprime / 12.92f : MathF.Pow((Rprime + 0.055f) / 1.055f, 2.4f)) * 80.0f;
        }
    }

    // A 10-bit coded Gamma 2.2 would have precision issues compared to PQ, so we don't want to define [0..1023] to
    // extend all the way to 10K nits. Instead, we define codepoints 0..255 as for an 8-bit Gamma2.2, and define the
    // maximum value as (1023/255)^2.2 * 80 = ~1700 nits.
    private class Gamma22 : EOTF
    {
        public Gamma22() : base("Gamma 2.2 (extended)")
        {
        }

        public override float ToCode(float nits)
        {
            return MathF.Pow(nits * 0.0125f, 1.0f / 2.2f) * 255.0f;
        }

        public override float ToNits(float signal)
        {
            return MathF.Pow(signal / 255.0f, 2.2f) * 80.0f;
        }
    }

    private class Gamma24 : EOTF
    {
        public Gamma24() : base("Gamma 2.4 (extended)")
        {
        }

        public override float ToCode(float nits)
        {
            return MathF.Pow(nits * 0.0125f, 1.0f / 2.4f) * 255.0f;
        }

        public override float ToNits(float signal)
        {
            return MathF.Pow(signal / 255.0f, 2.4f) * 80.0f;
        }
    }

}
