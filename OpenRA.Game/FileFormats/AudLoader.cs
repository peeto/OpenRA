#region Copyright & License Information
/*
 * Copyright 2007-2015 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation. For more information,
 * see COPYING.
 */
#endregion

using System;
using System.IO;

namespace OpenRA.FileFormats
{
	[Flags]
	enum SoundFlags
	{
		Stereo = 0x1,
		_16Bit = 0x2,
	}

	enum SoundFormat
	{
		WestwoodCompressed = 1,
		ImaAdpcm = 99,
	}

	struct Chunk
	{
		public int CompressedSize;
		public int OutputSize;

		public static Chunk Read(Stream s)
		{
			Chunk c;
			c.CompressedSize = s.ReadUInt16();
			c.OutputSize = s.ReadUInt16();

			if (s.ReadUInt32() != 0xdeaf)
				throw new InvalidDataException("Chunk header is bogus");
			return c;
		}
	}

	public class AudLoader : ISoundLoader
	{
		static readonly int[] IndexAdjust = { -1, -1, -1, -1, 2, 4, 6, 8 };
		static readonly int[] StepTable =
		{
			7, 8, 9, 10, 11, 12, 13, 14, 16,
			17, 19, 21, 23, 25, 28, 31, 34, 37,
			41, 45, 50, 55, 60, 66, 73, 80, 88,
			97, 107, 118, 130, 143, 157, 173, 190, 209,
			230, 253, 279, 307, 337, 371, 408, 449, 494,
			544, 598, 658, 724, 796, 876, 963, 1060, 1166,
			1282, 1411, 1552, 1707, 1878, 2066, 2272, 2499, 2749,
			3024, 3327, 3660, 4026, 4428, 4871, 5358, 5894, 6484,
			7132, 7845, 8630, 9493, 10442, 11487, 12635, 13899, 15289,
			16818, 18500, 20350, 22385, 24623, 27086, 29794, 32767
		};

		static short DecodeSample(byte b, ref int index, ref int current)
		{
			var sb = (b & 8) != 0;
			b &= 7;

			var delta = (StepTable[index] * b) / 4 + StepTable[index] / 8;
			if (sb) delta = -delta;

			current += delta;
			if (current > short.MaxValue) current = short.MaxValue;
			if (current < short.MinValue) current = short.MinValue;

			index += IndexAdjust[b];
			if (index < 0) index = 0;
			if (index > 88) index = 88;

			return (short)current;
		}

		public static byte[] LoadSound(byte[] raw, ref int index)
		{
			var s = new MemoryStream(raw);
			var dataSize = raw.Length;
			var outputSize = raw.Length * 4;

			var output = new byte[outputSize];
			var offset = 0;
			var currentSample = 0;

			while (dataSize-- > 0)
			{
				var b = s.ReadUInt8();

				var t = DecodeSample(b, ref index, ref currentSample);
				output[offset++] = (byte)t;
				output[offset++] = (byte)(t >> 8);

				t = DecodeSample((byte)(b >> 4), ref index, ref currentSample);
				output[offset++] = (byte)t;
				output[offset++] = (byte)(t >> 8);
			}

			return output;
		}

		public static float SoundLength(Stream s)
		{
			var sampleRate = s.ReadUInt16();
			/*var dataSize = */ s.ReadInt32();
			var outputSize = s.ReadInt32();
			var flags = (SoundFlags)s.ReadByte();

			var samples = outputSize;
			if (0 != (flags & SoundFlags.Stereo)) samples /= 2;
			if (0 != (flags & SoundFlags._16Bit)) samples /= 2;
			return samples / sampleRate;
		}

		public static bool LoadSound(Stream s, out byte[] rawData, out int sampleRate)
		{
			rawData = null;

			sampleRate = s.ReadUInt16();
			var dataSize = s.ReadInt32();
			var outputSize = s.ReadInt32();
			var readFlag = s.ReadByte();
			var readFormat = s.ReadByte();

			if (!Enum.IsDefined(typeof(SoundFlags), readFlag))
				return false;

			if (!Enum.IsDefined(typeof(SoundFormat), readFormat))
				return false;

			var output = new byte[outputSize];
			var offset = 0;
			var index = 0;
			var currentSample = 0;

			while (dataSize > 0)
			{
				var chunk = Chunk.Read(s);
				for (var n = 0; n < chunk.CompressedSize; n++)
				{
					var b = s.ReadUInt8();

					var t = DecodeSample(b, ref index, ref currentSample);
					output[offset++] = (byte)t;
					output[offset++] = (byte)(t >> 8);

					if (offset < outputSize)
					{
						/* possible that only half of the final byte is used! */
						t = DecodeSample((byte)(b >> 4), ref index, ref currentSample);
						output[offset++] = (byte)t;
						output[offset++] = (byte)(t >> 8);
					}
				}

				dataSize -= 8 + chunk.CompressedSize;
			}

			rawData = output;
			return true;
		}

		public bool TryParseSound(Stream stream, string fileName, out byte[] rawData, out int channels, out int sampleBits,
			out int sampleRate)
		{
			channels = sampleBits = sampleRate = 0;

			try
			{
				if (!LoadSound(stream, out rawData, out sampleRate))
					return false;
			}
			catch (Exception e)
			{
				// LoadSound() will check if the stream is in a format that this parser supports.
				// If not, it will simply return false so we know we can't use it. If it is, it will start
				// parsing the data without any further failsafes, which means that it will crash on corrupted files
				// (that end prematurely or otherwise don't conform to the specifications despite the headers being OK).
				Log.Write("debug", "Failed to parse AUD file {0}. Error message:".F(fileName));
				Log.Write("debug", e.ToString());
				rawData = null;
				return false;
			}

			channels = 1;
			sampleBits = 16;

			return true;
		}
	}
}
