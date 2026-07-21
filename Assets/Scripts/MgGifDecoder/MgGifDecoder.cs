#define mgGIF_UNSAFE

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices; // unsafe
using System.Text;
using UnityEngine;

namespace MG.GIF
{
    ////////////////////////////////////////////////////////////////////////////////

    public class Image : ICloneable
    {
        public Image()
        {
        }

        public Image(Image img)
        {
            this.Width = img.Width;
            this.Height = img.Height;
            this.Delay = img.Delay;
            this.RawImage = img.RawImage != null ? (Color32[])img.RawImage.Clone() : null;
        }

        public int Width { get; set; }

        public int Height { get; set; }

        public int Delay { get; set; } // milliseconds

        public Color32[] RawImage { get; set; }

        public object Clone()
        {
            return new Image(this);
        }

        public Texture2D CreateTexture()
        {
            var tex = new Texture2D(Width, Height, TextureFormat.ARGB32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };

            tex.SetPixels32(RawImage);
            tex.Apply();

            return tex;
        }
    }

    ////////////////////////////////////////////////////////////////////////////////

#if mgGIF_UNSAFE
    public unsafe class Decoder : IDisposable
#else
    public class Decoder : IDisposable
#endif
    {
        [Flags]
        private enum ImageFlag
        {
            Interlaced = 0x40,
            ColourTable = 0x80,
            TableSizeMask = 0x07,
            BitDepthMask = 0x70,
        }

        private enum Block
        {
            Image = 0x2C,
            Extension = 0x21,
            End = 0x3B,
        }

        private enum Extension
        {
            GraphicControl = 0xF9,
            Comments = 0xFE,
            PlainText = 0x01,
            ApplicationData = 0xFF,
        }

        private enum Disposal
        {
            None = 0x00,
            DoNotDispose = 0x04,
            RestoreBackground = 0x08,
            ReturnToPrevious = 0x0C,
        }

        [Flags]
        private enum ControlFlags
        {
            HasTransparency = 0x01,
            DisposalMask = 0x0C,
        }

        private const uint NoCode = 0xFFFF;
        private const ushort NoTransparency = 0xFFFF;

        private readonly int[] pow2 = { 1, 2, 4, 8, 16, 32, 64, 128, 256, 512, 1024, 2048, 4096 };

        // input stream to decode
        private byte[] input;
        private int d;

        // colour table
        private Color32[] globalColourTable;
        private Color32[] localColourTable;
        private Color32[] activeColourTable;
        private ushort transparentIndex;

        // current image
        private Image image = new Image();
        private ushort imageLeft;
        private ushort imageTop;
        private ushort imageWidth;
        private ushort imageHeight;

        private Color32[] output;
        private Color32[] previousImage;

        private bool disposed = false;

#if mgGIF_UNSAFE
        private int codesLength;
        private IntPtr codesHandle;
        private ushort* pCodes;

        private IntPtr curBlockHandle;
        private uint* pCurBlock;

        private const int MaxCodes = 4096;
        private IntPtr indicesHandle;
        private ushort** pIndicies;
#else
        private int[] indices = new int[4096];
        private ushort[] codes = new ushort[128 * 1024];
        private uint[] curBlock = new uint[64];
#endif

        public Decoder(byte[] data)
            : this()
        {
            this.Load(data);
        }

#if mgGIF_UNSAFE
        public Decoder()
        {
            // unmanaged allocations
            this.codesLength = 128 * 1024;
            this.codesHandle = Marshal.AllocHGlobal(this.codesLength * sizeof(ushort));
            this.pCodes = (ushort*)this.codesHandle.ToPointer();

            this.curBlockHandle = Marshal.AllocHGlobal(64 * sizeof(uint));
            this.pCurBlock = (uint*)this.curBlockHandle.ToPointer();

            this.indicesHandle = Marshal.AllocHGlobal(MaxCodes * sizeof(ushort*));
            this.pIndicies = (ushort**)this.indicesHandle.ToPointer();
        }

        ~Decoder()
        {
            this.Dispose(false);
        }
#else
        public Decoder()
        {
        }
#endif

        public string Version { get; private set; }

        public ushort Width { get; private set; }

        public ushort Height { get; private set; }

        public Color32 BackgroundColour { get; private set; }

        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        public Decoder Load(byte[] data)
        {
            this.input = data;
            this.d = 0;

            this.globalColourTable = new Color32[256];
            this.localColourTable = new Color32[256];
            this.transparentIndex = NoTransparency;
            this.output = null;
            this.previousImage = null;

            this.image.Delay = 0;

            return this;
        }

        public Image NextImage()
        {
            // if at start of data, read header

            if (this.d == 0)
            {
                this.ReadHeader();
            }

            // read blocks until we find an image block

            while (true)
            {
                var block = (Block)this.ReadByte();

                switch (block)
                {
                    case Block.Image:
                        {
                            // return the image if we got one

                            var img = this.ReadImageBlock();

                            if (img != null)
                            {
                                return img;
                            }
                        }

                        break;

                    case Block.Extension:
                        {
                            var ext = (Extension)this.ReadByte();

                            if (ext == Extension.GraphicControl)
                            {
                                this.ReadControlBlock();
                            }
                            else
                            {
                                this.SkipBlocks();
                            }
                        }

                        break;

                    case Block.End:
                        {
                            // end block - stop!
                            return null;
                        }

                    default:
                        {
                            throw new Exception("Unexpected block type");
                        }
                }
            }
        }

        public static string Ident()
        {
            const string v = "1.1";
            var e = BitConverter.IsLittleEndian ? "L" : "B";

#if ENABLE_IL2CPP
            var b = "N";
#else
            const string b = "M";
#endif

#if mgGIF_UNSAFE
            const string s = "U";
#else
            var s = "S";
#endif

#if NET_4_6
            var n = "4.x";
#else
            const string n = "2.0";
#endif

            return $"{v} {e}{s}{b} {n}";
        }

        protected virtual void Dispose(bool disposing)
        {
            if (this.disposed)
            {
                return;
            }

#if mgGIF_UNSAFE
            // release unmanaged resources
            Marshal.FreeHGlobal(this.codesHandle);
            Marshal.FreeHGlobal(this.curBlockHandle);
            Marshal.FreeHGlobal(this.indicesHandle);
#endif

            this.disposed = true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private byte ReadByte()
        {
            return this.input[this.d++];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ushort ReadUInt16()
        {
            return (ushort)(this.input[this.d++] | (this.input[this.d++] << 8));
        }

        private void ReadHeader()
        {
            if (this.input == null || this.input.Length <= 12)
            {
                throw new Exception("Invalid data");
            }

            // signature

            this.Version = Encoding.ASCII.GetString(this.input, 0, 6);
            this.d = 6;

            if (this.Version != "GIF87a" && this.Version != "GIF89a")
            {
                throw new Exception("Unsupported GIF version");
            }

            // read header

            this.Width = this.ReadUInt16();
            this.Height = this.ReadUInt16();

            this.image.Width = this.Width;
            this.image.Height = this.Height;

            var flags = (ImageFlag)this.ReadByte();
            var bgIndex = this.ReadByte(); // background colour

            this.ReadByte(); // aspect ratio

            if (flags.HasFlag(ImageFlag.ColourTable))
            {
                this.ReadColourTable(this.globalColourTable, flags);
            }

            this.BackgroundColour = this.globalColourTable[bgIndex];
        }

        private Color32[] ReadColourTable(Color32[] colourTable, ImageFlag flags)
        {
            var tableSize = this.pow2[(int)(flags & ImageFlag.TableSizeMask) + 1];

            for (var i = 0; i < tableSize; i++)
            {
                colourTable[i] = new Color32(
                    this.input[this.d++],
                    this.input[this.d++],
                    this.input[this.d++],
                    0xFF);
            }

            return colourTable;
        }

        private void SkipBlocks()
        {
            var blockSize = this.input[this.d++];

            while (blockSize != 0x00)
            {
                this.d += blockSize;
                blockSize = this.input[this.d++];
            }
        }

        private void ReadControlBlock()
        {
            // read block

            this.ReadByte();                             // block size (0x04)
            var flags = (ControlFlags)this.ReadByte();  // flags
            this.image.Delay = this.ReadUInt16() * 10;        // delay (1/100th -> milliseconds)
            var transparentColour = this.ReadByte();     // transparent colour
            this.ReadByte();                             // terminator (0x00)

            // has transparent colour?

            if (flags.HasFlag(ControlFlags.HasTransparency))
            {
                this.transparentIndex = transparentColour;
            }
            else
            {
                this.transparentIndex = NoTransparency;
            }

            // dispose of current image

            switch ((Disposal)(flags & ControlFlags.DisposalMask))
            {
                default:
                case Disposal.None:
                case Disposal.DoNotDispose:
                    // remember current image in case we need to "return to previous"
                    this.previousImage = this.output;
                    break;

                case Disposal.RestoreBackground:
                    // empty image - don't track
                    this.output = new Color32[this.Width * this.Height];
                    break;

                case Disposal.ReturnToPrevious:

                    // return to previous image

                    this.output = new Color32[this.Width * this.Height];

                    if (this.previousImage != null)
                    {
                        Array.Copy(this.previousImage, this.output, this.output.Length);
                    }

                    break;
            }
        }

        private Image ReadImageBlock()
        {
            // read image block header

            this.imageLeft = this.ReadUInt16();
            this.imageTop = this.ReadUInt16();
            this.imageWidth = this.ReadUInt16();
            this.imageHeight = this.ReadUInt16();
            var flags = (ImageFlag)this.ReadByte();

            // bad image if we don't have any dimensions

            if (this.imageWidth == 0 || this.imageHeight == 0)
            {
                return null;
            }

            // read colour table

            if (flags.HasFlag(ImageFlag.ColourTable))
            {
                this.activeColourTable = this.ReadColourTable(this.localColourTable, flags);
            }
            else
            {
                this.activeColourTable = this.globalColourTable;
            }

            if (this.output == null)
            {
                this.output = new Color32[this.Width * this.Height];
                this.previousImage = this.output;
            }

            // read image data

            this.DecompressLZW();

            // deinterlace

            if (flags.HasFlag(ImageFlag.Interlaced))
            {
                this.Deinterlace();
            }

            // return image

            this.image.RawImage = this.output;
            return this.image;
        }

        private void Deinterlace()
        {
            var numRows = this.output.Length / this.Width;
            var writePos = this.output.Length - this.Width; // NB: work backwards due to Y-coord flip
            var deinterlaceInput = this.output;

            this.output = new Color32[this.output.Length];

            for (var row = 0; row < numRows; row++)
            {
                int copyRow;

                // every 8th row starting at 0
                if (row % 8 == 0)
                {
                    copyRow = row / 8;
                }
                else if ((row + 4) % 8 == 0)
                {
                    // every 8th row starting at 4
                    var o = numRows / 8;
                    copyRow = o + ((row - 4) / 8);
                }
                else if ((row + 2) % 4 == 0)
                {
                    // every 4th row starting at 2
                    var o = numRows / 4;
                    copyRow = o + ((row - 2) / 4);
                }
                else
                {
                    // every 2nd row starting at 1
                    // if( ( r + 1 ) % 2 == 0 )
                    var o = numRows / 2;
                    copyRow = o + ((row - 1) / 2);
                }

                Array.Copy(deinterlaceInput, (numRows - copyRow - 1) * this.Width, this.output, writePos, this.Width);

                writePos -= this.Width;
            }
        }

#if mgGIF_UNSAFE
        private void DecompressLZW()
        {
            var pCodeBufferEnd = this.pCodes + this.codesLength;

            fixed (byte* pData = this.input)
            {
                fixed (Color32* pOutput = this.output, pColourTable = this.activeColourTable)
                {
                    var row = (this.Height - this.imageTop - 1) * this.Width; // start at end of array as we are reversing the row order
                    var safeWidth = (this.imageLeft + this.imageWidth) > this.Width ? (this.Width - this.imageLeft) : this.imageWidth;

                    var pWrite = &pOutput[row + this.imageLeft];
                    var pRow = pWrite;
                    var pRowEnd = pWrite + this.imageWidth;
                    var pImageEnd = pWrite + safeWidth;

                    // setup codes

                    int minimumCodeSize = this.input[this.d++];

                    if (minimumCodeSize > 11)
                    {
                        minimumCodeSize = 11;
                    }

                    var codeSize = minimumCodeSize + 1;
                    var nextSize = this.pow2[codeSize];
                    var maximumCodeSize = this.pow2[minimumCodeSize];
                    var clearCode = (uint)maximumCodeSize;
                    var endCode = (uint)(maximumCodeSize + 1);

                    // initialise buffers

                    var numCodes = maximumCodeSize + 2;
                    var pCodesEnd = this.pCodes;

                    for (ushort i = 0; i < numCodes; i++)
                    {
                        this.pIndicies[i] = pCodesEnd;
                        *pCodesEnd++ = 1;
                        *pCodesEnd++ = i;
                    }

                    // LZW decode loop

                    uint previousCode = NoCode;   // last code processed
                    uint mask = (uint)(nextSize - 1); // mask out code bits
                    uint shiftRegister = 0;        // shift register holds the bytes coming in from the input stream, we shift down by the number of bits

                    int bitsAvailable = 0;        // number of bits available to read in the shift register
                    int bytesAvailable = 0;        // number of bytes left in current block

                    uint* pD = this.pCurBlock;           // pointer to next bits in current block

                    while (true)
                    {
                        // get next code

                        uint curCode = shiftRegister & mask;

                        // did we read enough bits?

                        if (bitsAvailable >= codeSize)
                        {
                            // we had enough bits in the shift register so shunt it down
                            bitsAvailable -= codeSize;
                            shiftRegister >>= codeSize;
                        }
                        else
                        {
                            // not enough bits in register, so get more

                            // if start of new block

                            if (bytesAvailable <= 0)
                            {
                                // read blocksize

                                var pBlock = &pData[this.d++];
                                bytesAvailable = (int)(*pBlock++);
                                this.d += bytesAvailable;

                                // exit if end of stream

                                if (bytesAvailable == 0)
                                {
                                    return;
                                }

                                // copy block into buffer

                                this.pCurBlock[(bytesAvailable - 1) / 4] = 0; // zero last entry
                                Buffer.MemoryCopy(pBlock, this.pCurBlock, 256, bytesAvailable);

                                // reset data pointer
                                pD = this.pCurBlock;
                            }

                            // load shift register from data pointer

                            shiftRegister = *pD++;
                            int newBits = bytesAvailable >= 4 ? 32 : bytesAvailable * 8;
                            bytesAvailable -= 4;

                            // read remaining bits

                            if (bitsAvailable > 0)
                            {
                                var bitsRemaining = codeSize - bitsAvailable;
                                curCode |= (shiftRegister << bitsAvailable) & mask;
                                shiftRegister >>= bitsRemaining;
                                bitsAvailable = newBits - bitsRemaining;
                            }
                            else
                            {
                                curCode = shiftRegister & mask;
                                shiftRegister >>= codeSize;
                                bitsAvailable = newBits - codeSize;
                            }
                        }

                        // process code

                        if (curCode == clearCode)
                        {
                            // reset codes
                            codeSize = minimumCodeSize + 1;
                            nextSize = this.pow2[codeSize];
                            numCodes = maximumCodeSize + 2;

                            // reset buffer write pos
                            pCodesEnd = &this.pCodes[numCodes * 2];

                            // clear previous code
                            previousCode = NoCode;
                            mask = (uint)(nextSize - 1);

                            continue;
                        }
                        else if (curCode == endCode)
                        {
                            // stop
                            break;
                        }

                        bool plusOne = false;
                        ushort* pCodePos = null;

                        if (curCode < numCodes)
                        {
                            // write existing code
                            pCodePos = this.pIndicies[curCode];
                        }
                        else if (previousCode != NoCode)
                        {
                            // write previous code
                            pCodePos = this.pIndicies[previousCode];
                            plusOne = true;
                        }
                        else
                        {
                            continue;
                        }

                        // output colours

                        var codeLength = *pCodePos++;
                        var newCode = *pCodePos;
                        var pEnd = pCodePos + codeLength;

                        do
                        {
                            var code = *pCodePos++;

                            if (code != this.transparentIndex && pWrite < pImageEnd)
                            {
                                *pWrite = pColourTable[code];
                            }

                            if (++pWrite == pRowEnd)
                            {
                                pRow -= this.Width;
                                pWrite = pRow;
                                pRowEnd = pRow + this.imageWidth;
                                pImageEnd = pRow + safeWidth;

                                if (pWrite < pOutput)
                                {
                                    this.SkipBlocks();
                                    return;
                                }
                            }
                        }
                        while (pCodePos < pEnd);

                        if (plusOne)
                        {
                            if (newCode != this.transparentIndex && pWrite < pImageEnd)
                            {
                                *pWrite = pColourTable[newCode];
                            }

                            if (++pWrite == pRowEnd)
                            {
                                pRow -= this.Width;
                                pWrite = pRow;
                                pRowEnd = pRow + this.imageWidth;
                                pImageEnd = pRow + safeWidth;

                                if (pWrite < pOutput)
                                {
                                    break;
                                }
                            }
                        }

                        // create new code

                        if (previousCode != NoCode && numCodes != MaxCodes)
                        {
                            // get previous code from buffer

                            pCodePos = this.pIndicies[previousCode];
                            codeLength = *pCodePos++;

                            // resize buffer if required (should be rare)

                            if (pCodesEnd + codeLength + 1 >= pCodeBufferEnd)
                            {
                                var pBase = this.pCodes;

                                // realloc buffer
                                this.codesLength *= 2;
                                this.codesHandle = Marshal.ReAllocHGlobal(this.codesHandle, (IntPtr)(this.codesLength * sizeof(ushort)));

                                this.pCodes = (ushort*)this.codesHandle.ToPointer();
                                pCodeBufferEnd = this.pCodes + this.codesLength;

                                // rebase pointers

                                pCodesEnd = this.pCodes + (pCodesEnd - pBase);

                                for (int i = 0; i < numCodes; i++)
                                {
                                    this.pIndicies[i] = this.pCodes + (this.pIndicies[i] - pBase);
                                }

                                pCodePos = this.pIndicies[previousCode];
                                pCodePos++;
                            }

                            // add new code

                            this.pIndicies[numCodes++] = pCodesEnd;
                            *pCodesEnd++ = (ushort)(codeLength + 1);

                            // copy previous code sequence

                            Buffer.MemoryCopy(pCodePos, pCodesEnd, (long)codeLength * sizeof(ushort), (long)codeLength * sizeof(ushort));
                            pCodesEnd += codeLength;

                            // append new code

                            *pCodesEnd++ = newCode;
                        }

                        // increase code size?

                        if (numCodes >= nextSize && codeSize < 12)
                        {
                            nextSize = this.pow2[++codeSize];
                            mask = (uint)(nextSize - 1);
                        }

                        // remember last code processed
                        previousCode = curCode;
                    }

                    // consume any remaining blocks
                    this.SkipBlocks();
                }
            }
        }
#else
        private void DecompressLZW()
        {
            // output write position

            int row = (this.Height - this.imageTop - 1) * this.Width; // reverse rows for unity texture coords
            int col = this.imageLeft;
            int rightEdge = this.imageLeft + this.imageWidth;

            // setup codes

            int minimumCodeSize = this.input[this.d++];

            if (minimumCodeSize > 11)
            {
                minimumCodeSize = 11;
            }

            var codeSize = minimumCodeSize + 1;
            var nextSize = this.pow2[codeSize];
            var maximumCodeSize = this.pow2[minimumCodeSize];
            var clearCode = (uint)maximumCodeSize;
            var endCode = (uint)(maximumCodeSize + 1);

            // initialise buffers

            var codesEnd = 0;
            var numCodes = maximumCodeSize + 2;

            for (ushort i = 0; i < numCodes; i++)
            {
                this.indices[i] = codesEnd;
                this.codes[codesEnd++] = 1; // length
                this.codes[codesEnd++] = i; // code
            }

            // LZW decode loop

            uint previousCode = NoCode; // last code processed
            uint mask = (uint)(nextSize - 1); // mask out code bits
            uint shiftRegister = 0; // shift register holds the bytes coming in from the input stream, we shift down by the number of bits

            int bitsAvailable = 0; // number of bits available to read in the shift register
            int bytesAvailable = 0; // number of bytes left in current block

            int blockPos = 0;

            while (true)
            {
                // get next code

                uint curCode = shiftRegister & mask;

                if (bitsAvailable >= codeSize)
                {
                    bitsAvailable -= codeSize;
                    shiftRegister >>= codeSize;
                }
                else
                {
                    // reload shift register

                    // if start of new block

                    if (bytesAvailable <= 0)
                    {
                        // read blocksize
                        bytesAvailable = this.input[this.d++];

                        // exit if end of stream
                        if (bytesAvailable == 0)
                        {
                            return;
                        }

                        // read block
                        this.curBlock[(bytesAvailable - 1) / 4] = 0; // zero last entry
                        Buffer.BlockCopy(this.input, this.d, this.curBlock, 0, bytesAvailable);
                        blockPos = 0;
                        this.d += bytesAvailable;
                    }

                    // load shift register

                    shiftRegister = this.curBlock[blockPos++];
                    int newBits = bytesAvailable >= 4 ? 32 : bytesAvailable * 8;
                    bytesAvailable -= 4;

                    // read remaining bits

                    if (bitsAvailable > 0)
                    {
                        var bitsRemaining = codeSize - bitsAvailable;
                        curCode |= (shiftRegister << bitsAvailable) & mask;
                        shiftRegister >>= bitsRemaining;
                        bitsAvailable = newBits - bitsRemaining;
                    }
                    else
                    {
                        curCode = shiftRegister & mask;
                        shiftRegister >>= codeSize;
                        bitsAvailable = newBits - codeSize;
                    }
                }

                // process code

                if (curCode == clearCode)
                {
                    // reset codes
                    codeSize = minimumCodeSize + 1;
                    nextSize = this.pow2[codeSize];
                    numCodes = maximumCodeSize + 2;

                    // reset buffer write pos
                    codesEnd = numCodes * 2;

                    // clear previous code
                    previousCode = NoCode;
                    mask = (uint)(nextSize - 1);

                    continue;
                }
                else if (curCode == endCode)
                {
                    // stop
                    break;
                }

                bool plusOne = false;
                int codePos = 0;

                if (curCode < numCodes)
                {
                    // write existing code
                    codePos = this.indices[curCode];
                }
                else if (previousCode != NoCode)
                {
                    // write previous code
                    codePos = this.indices[previousCode];
                    plusOne = true;
                }
                else
                {
                    continue;
                }

                // output colours

                var codeLength = this.codes[codePos++];
                var newCode = this.codes[codePos];

                for (int i = 0; i < codeLength; i++)
                {
                    var code = this.codes[codePos++];

                    if (code != this.transparentIndex && col < this.Width)
                    {
                        this.output[row + col] = this.activeColourTable[code];
                    }

                    if (++col == rightEdge)
                    {
                        col = this.imageLeft;
                        row -= this.Width;

                        if (row < 0)
                        {
                            this.SkipBlocks();
                            return;
                        }
                    }
                }

                if (plusOne)
                {
                    if (newCode != this.transparentIndex && col < this.Width)
                    {
                        this.output[row + col] = this.activeColourTable[newCode];
                    }

                    if (++col == rightEdge)
                    {
                        col = this.imageLeft;
                        row -= this.Width;

                        if (row < 0)
                        {
                            break;
                        }
                    }
                }

                // create new code

                if (previousCode != NoCode && numCodes != this.indices.Length)
                {
                    // get previous code from buffer

                    codePos = this.indices[previousCode];
                    codeLength = this.codes[codePos++];

                    // resize buffer if required (should be rare)

                    if (codesEnd + codeLength + 1 >= this.codes.Length)
                    {
                        Array.Resize(ref this.codes, this.codes.Length * 2);
                    }

                    // add new code

                    this.indices[numCodes++] = codesEnd;
                    this.codes[codesEnd++] = (ushort)(codeLength + 1);

                    // copy previous code sequence

                    var stop = codesEnd + codeLength;

                    while (codesEnd < stop)
                    {
                        this.codes[codesEnd++] = this.codes[codePos++];
                    }

                    // append new code

                    this.codes[codesEnd++] = newCode;
                }

                // increase code size?

                if (numCodes >= nextSize && codeSize < 12)
                {
                    nextSize = this.pow2[++codeSize];
                    mask = (uint)(nextSize - 1);
                }

                // remember last code processed
                previousCode = curCode;
            }

            // skip any remaining blocks
            this.SkipBlocks();
        }
#endif
    }
}
