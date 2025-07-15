using System;

namespace xDRCal;

public class Util
{
    public static float PQCodeToNits(int N)
    {
        float Nn_pow = MathF.Pow(N / 1023.0f, 32.0f / 2523.0f);

        float numerator = Math.Max(Nn_pow - 107.0f / 128.0f, 0.0f);
        float denominator = 2413.0f / 128.0f - 2392.0f / 128.0f * Nn_pow;
        
        return MathF.Pow(numerator / denominator, 8192.0f / 1305.0f) * 10000.0f;
    }
    public static float SrgbToLinear(float value)
    {
        if (value <= 0.04045f)
            return value / 12.92f;
        else
            return MathF.Pow((value + 0.055f) / 1.055f, 2.4f);
    }
}
