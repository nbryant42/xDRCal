namespace xDRCal;

using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Direct2D1.Effects;
using Vortice.DXGI;
using Vortice.Mathematics;

public partial class UltraHdrDecoder : IDisposable
{
    private readonly IntPtr _decoder;

    public UltraHdrDecoder()
    {
        _decoder = uhdr_create_decoder();
    }

    public void Dispose()
    {
        uhdr_release_decoder(_decoder);
        GC.SuppressFinalize(this);
    }

    // Build D2D effect graph to render the image. (Builds the refs, but does not set all values just yet.)
    public static ColorMatrix CreateEffect(ID2D1DeviceContext5 ctx, out CrossFade crossFade,
        out ColorManagement colorEffect)
    {
        crossFade = new CrossFade(ctx);
        colorEffect = new ColorManagement(ctx)
        {
            Quality = ColormanagementQuality.Best,
        };
        colorEffect.SetInputEffect(0, crossFade);

        var matrixEffect = new ColorMatrix(ctx);
        matrixEffect.SetInputEffect(0, colorEffect);
        return matrixEffect;
    }

    public static void UpdateEffect(ID2D1DeviceContext5 ctx, ID2D1Bitmap1 sdrBitmap, ID2D1Bitmap1 hdrBitmap,
        float peakHdrScrgb, CrossFade crossFade, ColorManagement colorEffect, ColorMatrix matrixEffect, bool hdr,
        IntPtr hwnd, float targetPeakScrgb)
    {
        var dip = Util.FindDeviceInterfacePath(hwnd);
        float whiteLevel;

        crossFade.SetInput(0, hdrBitmap, true);
        crossFade.SetInput(1, sdrBitmap, true);

        if (hdr)
        {
            // whiteLevel is the desktop brightness multiplier, minimum 1.0f
            whiteLevel = dip != null ? Util.GetSdrWhiteLevel(dip) : 1.0f;

            if (peakHdrScrgb <= 1.0f)
            {
                // the "HDR" version is not actually brighter than SDR?!?
                // unlikely, but must not divide by zero. Use the HDR version.
                crossFade.Weight = 1.0f;
            }
            else
            {
                // headroom (in f-stops) is [1.0..peakHdrScrgb]
                var headroom = Math.Max(1.0f, Math.Min(peakHdrScrgb, targetPeakScrgb / whiteLevel));
                // crossFade.Weight is [0.0..1.0]; scale linearly with headroom
                crossFade.Weight = (headroom - 1.0f) / (peakHdrScrgb - 1.0f);
            }
        }
        else
        {
            whiteLevel = 1.0f;
            crossFade.Weight = 0.0f;
        }

        // TODO deal with legacy ICC profiles for non-sRGB SDR.
        colorEffect.DestinationColorContext = ctx.CreateColorContextFromDxgiColorSpace(
            hdr ? ColorSpaceType.RgbFullG10NoneP709 : ColorSpaceType.RgbFullG22NoneP709);
        colorEffect.SourceColorContext = hdrBitmap.ColorContext;
        colorEffect.SetInputEffect(0, crossFade, true);

        // SDR white level scaling is performing by multiplying RGB color values in linear gamma.
        // We implement this with a Direct2D matrix effect.
        var matrix = new Matrix5x4(
            whiteLevel, 0, 0, 0,  // [R] Multiply each color channel
            0, whiteLevel, 0, 0,  // [G] by the scale factor in 
            0, 0, whiteLevel, 0,  // [B] linear gamma space.
            0, 0, 0, 1,           // [A] Preserve alpha values.
            0, 0, 0, 0);          //     No offset.

        matrixEffect.SetValue(0, matrix);
    }

    // not static; we are requiring the instance still exist so that decoder-owned memory won't be freed yet.
    public ID2D1Bitmap1 CreateBitmap(ID2D1DeviceContext5 ctx, ref uhdr_raw_image_t image, bool hdr, ID2D1ColorContext1 sourceColorContext)
    {
        var size = new SizeI((int)image.w, (int)image.h);

        var fmt = new PixelFormat(hdr ? Format.R16G16B16A16_Float : Format.R8G8B8A8_UNorm,
            Vortice.DCommon.AlphaMode.Premultiplied);

        var props = new BitmapProperties1(fmt, 96f, 96f, BitmapOptions.None, sourceColorContext);
        return CreateBitmap(ctx, ref image, size, props);
    }

    private static ID2D1Bitmap1 CreateBitmap(ID2D1DeviceContext5 ctx, ref uhdr_raw_image_t image, SizeI size,
        BitmapProperties1 props)
    {
        var pixBytes = props.PixelFormat.Format == Format.R16G16B16A16_Float ? 8U : 4U;
        return ctx.CreateBitmap(size, image.plane0, image.stride0 * pixBytes, props);
    }

    // not static; we are requiring the instance still exist so that decoder-owned memory won't be freed yet.
    public ID2D1ColorContext1 CreateColorContext(ID2D1DeviceContext5 ctx, ref uhdr_raw_image_t image,
        bool hdr)
    {
        switch (image.cg)
        {
            case uhdr_color_gamut_t.UHDR_CG_BT_709:
                return ctx.CreateColorContextFromDxgiColorSpace(
                    hdr ? ColorSpaceType.RgbFullG10NoneP709 : ColorSpaceType.RgbFullG22NoneP709);
            case uhdr_color_gamut_t.UHDR_CG_DISPLAY_P3:
                var simp = new SimpleColorProfile()
                {
                    RedPrimary = new Vector2(0.68f, 0.32f),
                    GreenPrimary = new Vector2(0.265f, 0.69f),
                    BluePrimary = new Vector2(0.15f, 0.06f),
                    // differs slightly from standard D65; I'm using the ICC values:
                    WhitePointXZ = new Vector2(0.9505f, 1.0891f),
                    Gamma = hdr ? Gamma1.G10 : Gamma1.G22
                };
                return ctx.CreateColorContextFromSimpleColorProfile(ref simp);
            case uhdr_color_gamut_t.UHDR_CG_BT_2100:
                // From Wikipedia. I'd like better confirmation of these values against an ICC.
                // DisplayCAL and ICCViewer can't parse primaries from the V4 profiles I have.
                simp = new SimpleColorProfile()
                {
                    RedPrimary = new Vector2(0.708f, 0.292f),
                    GreenPrimary = new Vector2(0.17f, 0.797f),
                    BluePrimary = new Vector2(0.131f, 0.046f),
                    // differs slightly from standard D65; I'm using the ICC values:
                    WhitePointXZ = new Vector2(0.9505f, 1.0891f),
                    Gamma = hdr ? Gamma1.G10 : Gamma1.G22
                };
                return ctx.CreateColorContextFromSimpleColorProfile(ref simp);
            default:
                throw new UltraHdrException("CreateColorContext", new uhdr_error_info_t()
                {
                    error_code = uhdr_codec_err_t.UHDR_CODEC_UNSUPPORTED_FEATURE,
                    has_detail = true,
                    detail = $"Unsupported gamut {image.cg}"
                });
        }
    }

    public unsafe uhdr_raw_image_t DecodeJpegGainmap(byte[] jpegBytes, bool hdr, float maxDisplayBoost)
    {
        uhdr_reset_decoder(_decoder);

        fixed (byte* jpegPtr = jpegBytes)
        {
            var compressed = new uhdr_compressed_image_t
            {
                data = (nint)jpegPtr,
                data_sz = (UIntPtr)jpegBytes.Length,
                capacity = (UIntPtr)jpegBytes.Length,
                cg = uhdr_color_gamut_t.UHDR_CG_UNSPECIFIED,
                ct = uhdr_color_transfer_t.UHDR_CT_UNSPECIFIED,
                range = uhdr_color_range_t.UHDR_CR_UNSPECIFIED
            };

            var err = uhdr_dec_set_image(_decoder, ref compressed);
            if (err.error_code != uhdr_codec_err_t.UHDR_CODEC_OK)
                throw new UltraHdrException("uhdr_dec_set_image", err);

            err = uhdr_dec_set_out_color_transfer(_decoder,
                hdr ? uhdr_color_transfer_t.UHDR_CT_LINEAR : uhdr_color_transfer_t.UHDR_CT_SRGB);

            if (err.error_code != uhdr_codec_err_t.UHDR_CODEC_OK)
                throw new UltraHdrException("uhdr_dec_set_out_color_transfer", err);

            err = uhdr_dec_set_out_img_format(_decoder,
                hdr ? uhdr_img_fmt_t.UHDR_IMG_FMT_64bppRGBAHalfFloat : uhdr_img_fmt_t.UHDR_IMG_FMT_32bppRGBA8888);

            if (err.error_code != uhdr_codec_err_t.UHDR_CODEC_OK)
                throw new UltraHdrException("uhdr_dec_set_out_img_format", err);

            if (maxDisplayBoost >= 1.0f)
            {
                err = uhdr_dec_set_out_max_display_boost(_decoder, (float)maxDisplayBoost);
                if (err.error_code != uhdr_codec_err_t.UHDR_CODEC_OK)
                    throw new UltraHdrException("uhdr_dec_set_out_max_display_boost", err);
            }

            err = uhdr_dec_probe(_decoder);
            if (err.error_code != uhdr_codec_err_t.UHDR_CODEC_OK)
                throw new UltraHdrException("uhdr_dec_probe", err);

            err = uhdr_decode(_decoder);
            if (err.error_code != uhdr_codec_err_t.UHDR_CODEC_OK)
                throw new UltraHdrException("uhdr_decode", err);

            var rawImage = uhdr_get_decoded_image(_decoder);
            if (rawImage == null)
                throw new UltraHdrException("uhdr_get_decoded_image", null);

            return *rawImage;
        }
    }

    private const string DllName = "uhdr.dll";

    public enum uhdr_img_fmt_t : int
    {
        UHDR_IMG_FMT_UNSPECIFIED = -1,
        UHDR_IMG_FMT_24bppYCbCrP010 = 0,
        UHDR_IMG_FMT_12bppYCbCr420 = 1,
        UHDR_IMG_FMT_8bppYCbCr400 = 2,
        UHDR_IMG_FMT_32bppRGBA8888 = 3,
        UHDR_IMG_FMT_64bppRGBAHalfFloat = 4,
        UHDR_IMG_FMT_32bppRGBA1010102 = 5,
        UHDR_IMG_FMT_24bppYCbCr444 = 6,
        UHDR_IMG_FMT_16bppYCbCr422 = 7,
        UHDR_IMG_FMT_16bppYCbCr440 = 8,
        UHDR_IMG_FMT_12bppYCbCr411 = 9,
        UHDR_IMG_FMT_10bppYCbCr410 = 10,
        UHDR_IMG_FMT_24bppRGB888 = 11,
        UHDR_IMG_FMT_30bppYCbCr444 = 12,
    }

    public enum uhdr_color_gamut_t : int
    {
        UHDR_CG_UNSPECIFIED = -1,
        UHDR_CG_BT_709 = 0,
        UHDR_CG_DISPLAY_P3 = 1,
        UHDR_CG_BT_2100 = 2,
    }

    public enum uhdr_color_transfer_t : int
    {
        UHDR_CT_UNSPECIFIED = -1,
        UHDR_CT_LINEAR = 0,
        UHDR_CT_HLG = 1,
        UHDR_CT_PQ = 2,
        UHDR_CT_SRGB = 3,
    }

    public enum uhdr_color_range_t : int
    {
        UHDR_CR_UNSPECIFIED = -1,
        UHDR_CR_LIMITED_RANGE = 0,
        UHDR_CR_FULL_RANGE = 1,
    }

    public enum uhdr_codec_err_t : int
    {
        UHDR_CODEC_OK,
        UHDR_CODEC_ERROR,
        UHDR_CODEC_UNKNOWN_ERROR,
        UHDR_CODEC_INVALID_PARAM,
        UHDR_CODEC_MEM_ERROR,
        UHDR_CODEC_INVALID_OPERATION,
        UHDR_CODEC_UNSUPPORTED_FEATURE,
        UHDR_CODEC_LIST_END,
    }

    //[StructLayout(LayoutKind.Sequential)]
    //private unsafe struct uhdr_mem_block_t
    //{
    //    private void* data;
    //    private UIntPtr data_sz;
    //    private UIntPtr capacity;
    //}

    [StructLayout(LayoutKind.Sequential)]
    private struct uhdr_compressed_image_t
    {
        internal IntPtr data;
        internal UIntPtr data_sz;
        internal UIntPtr capacity;
        internal uhdr_color_gamut_t cg;
        internal uhdr_color_transfer_t ct;
        internal uhdr_color_range_t range;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct uhdr_raw_image_t
    {
        internal uhdr_img_fmt_t fmt;
        internal uhdr_color_gamut_t cg;
        internal uhdr_color_transfer_t ct;
        internal uhdr_color_range_t range;
        internal uint w;
        internal uint h;
        internal IntPtr plane0;
        internal IntPtr plane1;
        internal IntPtr plane2;
        internal uint stride0;
        internal uint stride1;
        internal uint stride2;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct uhdr_error_info_t
    {
        public uhdr_codec_err_t error_code;
        [MarshalAs(UnmanagedType.Bool)]
        public bool has_detail;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string detail;

        public readonly override string ToString()
        {
            return has_detail ? $"{error_code}: {detail}" : error_code.ToString();
        }
    }

    // =====================
    // DECODER HANDLE (Opaque)
    // =====================
    private struct uhdr_codec_private_t { } // Opaque pointer type

    // =====================
    // P/Invoke Function Imports (Partial: Decoder only)
    // =====================

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr uhdr_create_decoder();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void uhdr_release_decoder(IntPtr dec);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern uhdr_error_info_t uhdr_dec_set_image(IntPtr dec, ref uhdr_compressed_image_t img);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern uhdr_error_info_t uhdr_dec_set_out_img_format(IntPtr dec, uhdr_img_fmt_t fmt);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern uhdr_error_info_t uhdr_dec_set_out_color_transfer(IntPtr dec, uhdr_color_transfer_t ct);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern uhdr_error_info_t uhdr_dec_set_out_max_display_boost(IntPtr dec, float display_boost);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern uhdr_error_info_t uhdr_decode(IntPtr dec);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static unsafe extern uhdr_raw_image_t* uhdr_get_decoded_image(IntPtr dec);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void uhdr_reset_decoder(IntPtr dec);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern uhdr_error_info_t uhdr_dec_probe(IntPtr decoder);
}

public class UltraHdrException(string method, UltraHdrDecoder.uhdr_error_info_t? err) :
    Exception($"{method} failed: {err}")
{
    public UltraHdrDecoder.uhdr_error_info_t? Err { get; } = err;
}