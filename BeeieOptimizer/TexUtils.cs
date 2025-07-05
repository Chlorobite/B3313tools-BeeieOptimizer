using System;

public static class TexUtils {
	public static void RGBA16_cutoutfix(ushort[] data, int width, int height) {
		// Kill any existing cutouts (possibly broken) first
		for (int y = 0; y < height; y++) {
			for (int x = 0; x < width; x++) {
				if ((data[y * width + x] & 1) == 0)
					data[y * width + x] = 0;
			}
		}

		// 2 cutout iterations are required to fully fix display, as with 1 iteration we don't fix a case of
		// (P = pixel, N = neighbor)
		//
		// v
		// ? N
		// N P
		for (int i = 0; i < 2; i++) {
			ushort[] newData = RGBA16_cutoutfix_iteration(data, width, height);
			Array.Copy(newData, data, data.Length);
		}
	}

	static ushort[] RGBA16_cutoutfix_iteration(ushort[] data, int width, int height) {
		ushort[] newData = new ushort[data.Length];
		Array.Copy(data, newData, data.Length);

		for (int y = 0; y < height; y++) {
			for (int x = 0; x < width; x++) {
				if (data[y * width + x] != 0) continue; // yknow dont like touch pixels with data

				uint mix_r = 0;
				uint mix_g = 0;
				uint mix_b = 0;
				uint mix_a = 0;

				RGBA16_cutoutfix_accumulate(data, width, height, x - 1, y, ref mix_r, ref mix_g, ref mix_b, ref mix_a);
				RGBA16_cutoutfix_accumulate(data, width, height, x + 1, y, ref mix_r, ref mix_g, ref mix_b, ref mix_a);
				RGBA16_cutoutfix_accumulate(data, width, height, x, y - 1, ref mix_r, ref mix_g, ref mix_b, ref mix_a);
				RGBA16_cutoutfix_accumulate(data, width, height, x, y + 1, ref mix_r, ref mix_g, ref mix_b, ref mix_a);

				if (mix_r > 0 && mix_g > 0 && mix_b > 0) {
					newData[y * width + x] = (ushort)(
						/*R*/ (((mix_r / mix_a) & 0x1Fu) << 11) |
						/*G*/ (((mix_g / mix_a) & 0x1Fu) << 6) |
						/*B*/ (((mix_b / mix_a) & 0x1Fu) << 1) |
						/*A*/ 0
					);
				}
			}
		}

		return newData;
	}

	static void RGBA16_cutoutfix_accumulate(ushort[] data, int width, int height, int x, int y, ref uint r, ref uint g, ref uint b, ref uint count) {
		// wrap around behavior, common in textures.
		if (x < 0) x += width;
		if (y < 0) y += height;
		if (x >= width) x -= width;
		if (y >= height) y -= height;

		ushort px = data[y * width + x];

		r += (uint)(px >> 11) & 0x1Fu;
		g += (uint)(px >> 6) & 0x1Fu;
		b += (uint)(px >> 1) & 0x1Fu;
		count += (px & 0xFFFF) != 0 ? 1u : 0u;
	}


	public static uint RiceCRC32(byte[] src, int width, int height, int size, int rowStride) {
		uint crc32Ret = 0;
		int bytesPerLine = (width << size) >> 1;

		try {
			int y = height - 1;
			int offset = 0;

			while (y >= 0) {
				uint esi = 0;
				int x = bytesPerLine - 4;

				while (x >= 0) {
					// Read 4 bytes as uint32 (little endian)
					esi = BitConverter.ToUInt32(src, offset + x);
					esi ^= (uint)x;

					crc32Ret = (crc32Ret << 4) + ((crc32Ret >> 28) & 0xF);
					crc32Ret += esi;

					x -= 4;
				}

				esi ^= (uint)y;
				crc32Ret += esi;

				offset += rowStride;
				y--;
			}
		}
		catch (Exception ex) {
			Console.WriteLine("Error: RiceCRC32 exception! " + ex.Message);
		}

		return crc32Ret;
	}
}
