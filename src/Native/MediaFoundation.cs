using System;
using System.Runtime.InteropServices;

// Media Foundation (mfplat/mfreadwrite) – nur das Noetigste zum Dekodieren von Audiodateien (MP3, AAC/M4A, WMA, FLAC ...).
// Reihenfolge der Methoden entspricht mfobjects.h / mfreadwrite.h. Nicht benutzte Methoden sind Platzhalter
// (sie muessen nur vorhanden sein, damit die Tabellen-Positionen stimmen).

namespace Mischpult.Native
{
    internal static class MF
    {
        public const int MF_VERSION = 0x00020070;
        public const int MF_SOURCE_READER_ALL_STREAMS = unchecked((int)0xFFFFFFFE);
        public const int MF_SOURCE_READER_FIRST_AUDIO_STREAM = unchecked((int)0xFFFFFFFD);
        public const int MF_SOURCE_READERF_ENDOFSTREAM = 0x2;

        public static readonly Guid MF_MT_MAJOR_TYPE = new Guid("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
        public static readonly Guid MF_MT_SUBTYPE = new Guid("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
        public static readonly Guid MF_MT_AUDIO_NUM_CHANNELS = new Guid("37e48bf5-645e-4c5b-89de-ada9e29b696a");
        public static readonly Guid MF_MT_AUDIO_SAMPLES_PER_SECOND = new Guid("5faeeae7-0290-4c31-9e8a-c534f68d9dba");
        public static readonly Guid MF_MT_AUDIO_BITS_PER_SAMPLE = new Guid("f2deb57f-40fa-4764-aa33-ed4f2d1ff669");
        public static readonly Guid MFMediaType_Audio = new Guid("73647561-0000-0010-8000-00AA00389B71");
        public static readonly Guid MFAudioFormat_Float = new Guid("00000003-0000-0010-8000-00AA00389B71");

        [DllImport("mfplat.dll", ExactSpelling = true)]
        public static extern int MFStartup(int version, int flags);

        [DllImport("mfplat.dll", ExactSpelling = true)]
        public static extern int MFShutdown();

        [DllImport("mfplat.dll", ExactSpelling = true)]
        public static extern int MFCreateMediaType(out IMFMediaType mediaType);

        [DllImport("mfreadwrite.dll", ExactSpelling = true)]
        public static extern int MFCreateSourceReaderFromURL([MarshalAs(UnmanagedType.LPWStr)] string url, IntPtr attributes, out IMFSourceReader reader);
    }

    [ComImport, Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMFMediaType
    {
        // IMFAttributes (30)
        [PreserveSig] int GetItem(ref Guid key, IntPtr value);
        [PreserveSig] int GetItemType(ref Guid key, out int type);
        [PreserveSig] int CompareItem(ref Guid key, IntPtr value, out int result);
        [PreserveSig] int Compare(IntPtr theirs, int matchType, out int result);
        [PreserveSig] int GetUINT32(ref Guid key, out int value);
        [PreserveSig] int GetUINT64(ref Guid key, out long value);
        [PreserveSig] int GetDouble(ref Guid key, out double value);
        [PreserveSig] int GetGUID(ref Guid key, out Guid value);
        [PreserveSig] int GetStringLength(ref Guid key, out int length);
        [PreserveSig] int GetString(ref Guid key, IntPtr value, int size, out int length);
        [PreserveSig] int GetAllocatedString(ref Guid key, out IntPtr value, out int length);
        [PreserveSig] int GetBlobSize(ref Guid key, out int size);
        [PreserveSig] int GetBlob(ref Guid key, IntPtr buf, int size, out int blobSize);
        [PreserveSig] int GetAllocatedBlob(ref Guid key, out IntPtr buf, out int size);
        [PreserveSig] int GetUnknown(ref Guid key, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int SetItem(ref Guid key, IntPtr value);
        [PreserveSig] int DeleteItem(ref Guid key);
        [PreserveSig] int DeleteAllItems();
        [PreserveSig] int SetUINT32(ref Guid key, int value);
        [PreserveSig] int SetUINT64(ref Guid key, long value);
        [PreserveSig] int SetDouble(ref Guid key, double value);
        [PreserveSig] int SetGUID(ref Guid key, ref Guid value);
        [PreserveSig] int SetString(ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);
        [PreserveSig] int SetBlob(ref Guid key, IntPtr buf, int size);
        [PreserveSig] int SetUnknown(ref Guid key, IntPtr unknown);
        [PreserveSig] int LockStore();
        [PreserveSig] int UnlockStore();
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetItemByIndex(int index, out Guid key, IntPtr value);
        [PreserveSig] int CopyAllItems(IntPtr dest);
        // IMFMediaType (5)
        [PreserveSig] int GetMajorType(out Guid type);
        [PreserveSig] int IsCompressedFormat(out int compressed);
        [PreserveSig] int IsEqual(IntPtr other, out int flags);
        [PreserveSig] int GetRepresentation(Guid rep, out IntPtr repr);
        [PreserveSig] int FreeRepresentation(Guid rep, IntPtr repr);
    }

    [ComImport, Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMFSample
    {
        // IMFAttributes (30) – nicht benutzt
        [PreserveSig] int A01(); [PreserveSig] int A02(); [PreserveSig] int A03(); [PreserveSig] int A04(); [PreserveSig] int A05();
        [PreserveSig] int A06(); [PreserveSig] int A07(); [PreserveSig] int A08(); [PreserveSig] int A09(); [PreserveSig] int A10();
        [PreserveSig] int A11(); [PreserveSig] int A12(); [PreserveSig] int A13(); [PreserveSig] int A14(); [PreserveSig] int A15();
        [PreserveSig] int A16(); [PreserveSig] int A17(); [PreserveSig] int A18(); [PreserveSig] int A19(); [PreserveSig] int A20();
        [PreserveSig] int A21(); [PreserveSig] int A22(); [PreserveSig] int A23(); [PreserveSig] int A24(); [PreserveSig] int A25();
        [PreserveSig] int A26(); [PreserveSig] int A27(); [PreserveSig] int A28(); [PreserveSig] int A29(); [PreserveSig] int A30();
        // IMFSample
        [PreserveSig] int GetSampleFlags(out int flags);
        [PreserveSig] int SetSampleFlags(int flags);
        [PreserveSig] int GetSampleTime(out long time);
        [PreserveSig] int SetSampleTime(long time);
        [PreserveSig] int GetSampleDuration(out long duration);
        [PreserveSig] int SetSampleDuration(long duration);
        [PreserveSig] int GetBufferCount(out int count);
        [PreserveSig] int GetBufferByIndex(int index, out IMFMediaBuffer buffer);
        [PreserveSig] int ConvertToContiguousBuffer(out IMFMediaBuffer buffer);
        [PreserveSig] int AddBuffer(IMFMediaBuffer buffer);
        [PreserveSig] int RemoveBufferByIndex(int index);
        [PreserveSig] int RemoveAllBuffers();
        [PreserveSig] int GetTotalLength(out int length);
        [PreserveSig] int CopyToBuffer(IMFMediaBuffer buffer);
    }

    [ComImport, Guid("045fa593-8799-42b8-bc8d-8968c6453507"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMFMediaBuffer
    {
        [PreserveSig] int Lock(out IntPtr buffer, out int maxLength, out int currentLength);
        [PreserveSig] int Unlock();
        [PreserveSig] int GetCurrentLength(out int length);
        [PreserveSig] int SetCurrentLength(int length);
        [PreserveSig] int GetMaxLength(out int length);
    }

    [ComImport, Guid("70ae66f2-c809-4e4f-8915-bdcb406b7993"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMFSourceReader
    {
        [PreserveSig] int GetStreamSelection(int streamIndex, out int selected);
        [PreserveSig] int SetStreamSelection(int streamIndex, int selected);
        [PreserveSig] int GetNativeMediaType(int streamIndex, int typeIndex, out IMFMediaType mediaType);
        [PreserveSig] int GetCurrentMediaType(int streamIndex, out IMFMediaType mediaType);
        [PreserveSig] int SetCurrentMediaType(int streamIndex, IntPtr reserved, IMFMediaType mediaType);
        [PreserveSig] int SetCurrentPosition(ref Guid timeFormat, IntPtr position);
        [PreserveSig] int ReadSample(int streamIndex, int controlFlags, out int actualStreamIndex, out int streamFlags, out long timestamp, out IMFSample sample);
        [PreserveSig] int Flush(int streamIndex);
        [PreserveSig] int GetServiceForStream(int streamIndex, ref Guid service, ref Guid riid, out IntPtr obj);
        [PreserveSig] int GetPresentationAttribute(int streamIndex, ref Guid attribute, IntPtr value);
    }

    /// <summary>2.19: Media Foundation zum Schreiben (MP3 ueber den Kodierer von Windows).</summary>
    internal static class MFW
    {
        public static readonly Guid MFAudioFormat_PCM = new Guid("00000001-0000-0010-8000-00AA00389B71");
        public static readonly Guid MFAudioFormat_MP3 = new Guid("00000055-0000-0010-8000-00AA00389B71");
        public static readonly Guid MF_MT_AUDIO_AVG_BYTES_PER_SECOND = new Guid("1aab75c8-cfef-451c-ab95-ac034b8e1731");
        public static readonly Guid MF_MT_AUDIO_BLOCK_ALIGNMENT = new Guid("322de230-9eeb-43bd-ab7a-ff412251541d");
        public static readonly Guid MF_MT_ALL_SAMPLES_INDEPENDENT = new Guid("c9173739-5e56-461c-b713-46fb995cb95f");

        [DllImport("mfreadwrite.dll", ExactSpelling = true)]
        public static extern int MFCreateSinkWriterFromURL([MarshalAs(UnmanagedType.LPWStr)] string url, IntPtr byteStream, IntPtr attributes, out IMFSinkWriter writer);

        [DllImport("mfplat.dll", ExactSpelling = true)]
        public static extern int MFCreateSample(out IMFSample sample);

        [DllImport("mfplat.dll", ExactSpelling = true)]
        public static extern int MFCreateMemoryBuffer(int maxLength, out IMFMediaBuffer buffer);
    }

    [ComImport, Guid("3137f1cd-fe5e-4805-a5d8-fb477448cb3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMFSinkWriter
    {
        [PreserveSig] int AddStream(IMFMediaType targetMediaType, out int streamIndex);
        [PreserveSig] int SetInputMediaType(int streamIndex, IMFMediaType inputMediaType, IntPtr encodingParameters);
        [PreserveSig] int BeginWriting();
        [PreserveSig] int WriteSample(int streamIndex, IMFSample sample);
        [PreserveSig] int SendStreamTick(int streamIndex, long timestamp);
        [PreserveSig] int PlaceMarker(int streamIndex, IntPtr context);
        [PreserveSig] int NotifyEndOfSegment(int streamIndex);
        [PreserveSig] int Flush(int streamIndex);
        [PreserveSig] int DoFinalize();
        [PreserveSig] int GetServiceForStream(int streamIndex, ref Guid service, ref Guid riid, out IntPtr obj);
        [PreserveSig] int GetStatistics(int streamIndex, IntPtr stats);
    }
}
