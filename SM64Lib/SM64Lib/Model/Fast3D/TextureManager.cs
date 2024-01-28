using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using global::System.Drawing;
using global::System.Drawing.Drawing2D;
using Microsoft.VisualBasic.CompilerServices;

namespace SM64Lib.Model.Fast3D
{
    public static class TextureManager
    {
        public static void PrepaireImage(ref Bitmap bmp, RotateFlipType rotateFlipTexture, N64Graphics.N64Codec texFormat, bool fitImageSize, TextureConverters converterToUse = TextureConverters.Internal)
        {
            if (fitImageSize)
            {
                int maxPixels = GetMaxPixls(texFormat);

                // Resize Texture
                if (bmp.Height * bmp.Width > maxPixels)
                {
                    int curPixels = bmp.Height * bmp.Width;
                    float verhälltnis = Conversions.ToSingle(Math.Sqrt(curPixels / (double)maxPixels));
                    float newHeight = bmp.Height / verhälltnis;
                    float newWidth = bmp.Width / verhälltnis;
                    int nhlog = Conversions.ToInteger(Math.Truncate(Math.Log(newHeight, 2)));
                    int nwlog = Conversions.ToInteger(Math.Truncate(Math.Log(newWidth, 2)));
                    newHeight = Conversions.ToSingle(Math.Pow(2, nhlog));
                    newWidth = Conversions.ToSingle(Math.Pow(2, nwlog));
                    bmp = (Bitmap)ResizeImage(bmp, new Size(Conversions.ToInteger(newWidth), Conversions.ToInteger(newHeight)), converterToUse:converterToUse);
                }
            }

            RotateFlipImage(bmp, rotateFlipTexture);
        }

        public static Image ResizeImage(Image image, Size size, bool preserveAspectRatio = false, bool forceSize = false, TextureConverters converterToUse = TextureConverters.Internal)
        {
            var result = new Size();

            if (preserveAspectRatio)
            {
                float val = (float)(image.Size.Width / (double)size.Width);
                float num = (float)(image.Size.Height / (double)size.Height);
                
                if (num > val)
                {
                    result.Width = Conversions.ToInteger(Math.Truncate(size.Width * val));
                    result.Height = size.Height;
                }
                else if (num < val)
                {
                    result.Width = size.Width;
                    result.Height = Conversions.ToInteger(Math.Truncate(size.Height * num));
                }
                else
                    result = size;
            }
            else
                result = size;
            
            var finalResult = forceSize ? size : result;
            Image newImage = new Bitmap(image, finalResult);

            bool needsPointToDraw() =>
                forceSize && result.Width / (double)result.Height != size.Width / (double)size.Height;

            Point getPointToDraw()
            {
                int px, py;
                px = (int)((size.Width - result.Width) / (double)2);
                py = (int)((size.Height - result.Height) / (double)2);
                return new Point(px, py);
            }

            switch (converterToUse)
            {
                case TextureConverters.Internal:
                    {
                        using (var g = Graphics.FromImage(newImage))
                        {
                            g.SmoothingMode = SmoothingMode.HighQuality;
                            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                            g.PageUnit = GraphicsUnit.Pixel;
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

                            Point pointToDraw;
                            if (needsPointToDraw())
                                pointToDraw = getPointToDraw();
                            else
                                pointToDraw = Point.Empty;

                            g.Clear(Color.Transparent);
                            g.DrawImage(image, new Rectangle(pointToDraw, result));
                            g.Dispose();
                        }
                    }
                    break;
                case TextureConverters.NConvert:
                    {
                        var arguments = new List<string>();
                        arguments.Add("-out png");
                        arguments.Add($"-resize {result.Width} {result.Height}");
                        arguments.Add("-overwrite");

                        var sourceFilePath = Path.GetTempFileName();
                        using (var fs = new FileStream(sourceFilePath, FileMode.Create, FileAccess.ReadWrite))
                            image.Save(fs, System.Drawing.Imaging.ImageFormat.Png);
                        arguments.Add($"-o \"{sourceFilePath}\"");
                        arguments.Add($"\"{sourceFilePath}\"");

                        var p = new Process();
                        p.StartInfo.CreateNoWindow = true;
                        p.StartInfo.FileName = FilePathsConfiguration.DefaultConfiguration.Files["nconvert.exe"];
                        p.StartInfo.Arguments = string.Join(" ", arguments.ToArray());

                        p.Start();
                        p.WaitForExit();

                        using (var fs = new FileStream(sourceFilePath, FileMode.Open, FileAccess.Read))
                            image = Image.FromStream(fs);

                        if (needsPointToDraw())
                        {
                            using (var g = Graphics.FromImage(newImage))
                            {
                                Point pointToDraw = getPointToDraw();
                                g.Clear(Color.Transparent);
                                g.DrawImage(image, new Rectangle(pointToDraw, result));
                                g.Dispose();
                            }
                        }
                    }
                    break;
            }

            return newImage;
        }

        public static void RotateFlipImage(Bitmap bmp, RotateFlipType rotateFlipTexture)
        {
            if (rotateFlipTexture != RotateFlipType.RotateNoneFlipNone)
            {
                bmp.RotateFlip(rotateFlipTexture);
            }
        }

        public static int GetMaxPixls(N64Graphics.N64Codec texFormat)
        {
            switch (texFormat)
            {
                case N64Graphics.N64Codec.CI4:
                    return 64 * 64;
                case N64Graphics.N64Codec.CI8:
                    return 32 * 64;
                case N64Graphics.N64Codec.I4:
                    return 128 * 64;
                case N64Graphics.N64Codec.I8:
                    return 64 * 64;
                case N64Graphics.N64Codec.IA4:
                    return 128 * 64;
                case N64Graphics.N64Codec.IA8:
                    return 64 * 64;
                case N64Graphics.N64Codec.IA16:
                    return 32 * 64;
                case N64Graphics.N64Codec.RGBA16:
                    return 32 * 64;
                case N64Graphics.N64Codec.RGBA32:
                    return 32 * 32;
                default:
                    return 32 * 32;
            }
        }
    }
}