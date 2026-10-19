using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace PingTray
{
    /// <summary>The overall connection state, shown as the color of the tray icon.</summary>
    public enum Status
    {
        Unknown,  // not checked yet, or no hosts to ping: gray
        Good,     // every host answers: green
        Partial,  // some hosts do not answer: yellow
        Down      // no host answers: red
    }

    /// <summary>
    /// Draws the status icons (a colored dot) as .ico data. The tray uses them at run time, and
    /// scripts/build.ps1 calls WriteIco to make the icon embedded in the exe, so both look the same.
    /// </summary>
    public static class StatusIcons
    {
        public static Color FillColor(Status status)
        {
            switch (status)
            {
                case Status.Good: return Color.FromArgb(0x2E, 0xA0, 0x43);
                case Status.Partial: return Color.FromArgb(0xF2, 0xC0, 0x1E);
                case Status.Down: return Color.FromArgb(0xE0, 0x3E, 0x3E);
                default: return Color.FromArgb(0x8C, 0x8C, 0x8C);
            }
        }

        public static Icon CreateIcon(Status status, int size)
        {
            using (var stream = new MemoryStream(IcoBytes(status, new[] { size })))
                return new Icon(stream, size, size);
        }

        public static void WriteIco(string path, Status status, int[] sizes)
        {
            File.WriteAllBytes(path, IcoBytes(status, sizes));
        }

        public static Bitmap Draw(Status status, int size)
        {
            var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);
                float border = Math.Max(1f, size / 16f);
                float inset = size / 16f + border / 2;
                var circle = new RectangleF(inset, inset, size - 2 * inset, size - 2 * inset);
                Color fill = FillColor(status);
                using (var brush = new SolidBrush(fill))
                    g.FillEllipse(brush, circle);
                using (var pen = new Pen(Color.FromArgb(fill.R * 6 / 10, fill.G * 6 / 10, fill.B * 6 / 10), border))
                    g.DrawEllipse(pen, circle);
            }
            return bitmap;
        }

        /// <summary>An .ico file with one image per size: 256 as PNG, smaller ones as 32-bit DIBs.</summary>
        public static byte[] IcoBytes(Status status, int[] sizes)
        {
            var images = new List<byte[]>();
            foreach (int size in sizes)
                using (Bitmap bitmap = Draw(status, size))
                    images.Add(size >= 256 ? Png(bitmap) : Dib(bitmap));

            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write((short)0);  // reserved
                writer.Write((short)1);  // type: icon
                writer.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    byte side = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);  // 0 means 256
                    writer.Write(side);
                    writer.Write(side);
                    writer.Write((byte)0);   // no palette
                    writer.Write((byte)0);   // reserved
                    writer.Write((short)1);  // color planes
                    writer.Write((short)32); // bits per pixel
                    writer.Write(images[i].Length);
                    writer.Write(offset);
                    offset += images[i].Length;
                }
                foreach (byte[] image in images)
                    writer.Write(image);
                writer.Flush();
                return stream.ToArray();
            }
        }

        static byte[] Png(Bitmap bitmap)
        {
            using (var stream = new MemoryStream())
            {
                bitmap.Save(stream, ImageFormat.Png);
                return stream.ToArray();
            }
        }

        static byte[] Dib(Bitmap bitmap)
        {
            int width = bitmap.Width, height = bitmap.Height;
            BitmapData data = bitmap.LockBits(new Rectangle(0, 0, width, height),
                ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            int stride = data.Stride;
            var pixels = new byte[stride * height];  // BGRA, top row first
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            bitmap.UnlockBits(data);

            // The AND mask marks fully transparent pixels; it matters only where alpha is ignored.
            int maskStride = (width + 31) / 32 * 4;
            var mask = new byte[maskStride * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    if (pixels[y * stride + x * 4 + 3] == 0)
                        mask[(height - 1 - y) * maskStride + x / 8] |= (byte)(0x80 >> (x % 8));

            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(40);           // BITMAPINFOHEADER size
                writer.Write(width);
                writer.Write(height * 2);   // the color image and the mask
                writer.Write((short)1);     // planes
                writer.Write((short)32);    // bits per pixel
                writer.Write(0);            // BI_RGB
                writer.Write(width * height * 4 + mask.Length);
                writer.Write(0);
                writer.Write(0);
                writer.Write(0);
                writer.Write(0);
                for (int y = height - 1; y >= 0; y--)  // DIB rows go bottom-up
                    writer.Write(pixels, y * stride, width * 4);
                writer.Write(mask);
                writer.Flush();
                return stream.ToArray();
            }
        }
    }
}
