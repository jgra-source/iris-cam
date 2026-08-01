//
// Reads frames published by the iPhone Webcam desktop app.
//
// This file runs INSIDE other programs - Teams, Chrome, Zoom - because that is
// where Windows loads a camera. If it misbehaves it takes their process with
// it, so it stays deliberately dull: no allocation, no exceptions, no waiting
// on anything, and it always fails quietly rather than throwing.
//
// The desktop app writes frames into shared memory. It alternates between two
// slots and bumps a counter before and after each write. We copy from whichever
// slot it points at, then check the counter did not move while we copied. If it
// did, our copy might be half of one frame and half of the next, so we discard
// it and try again. Because we never take a lock, a slow reader here can never
// slow the app down, and an app that has stopped simply looks like "no frame".
//
#pragma once
#ifndef SHARED_FRAME_READER_H
#define SHARED_FRAME_READER_H

#include <windows.h>
#include <cstdint>

class SharedFrameReader
{
public:
    SharedFrameReader() = default;
    ~SharedFrameReader() { Close(); }

    SharedFrameReader(const SharedFrameReader&) = delete;
    SharedFrameReader& operator=(const SharedFrameReader&) = delete;

    // Copies the newest frame into pDst, honouring pitch exactly as Media
    // Foundation asked for it (which may be negative for a bottom-up buffer).
    // Returns false whenever a good frame is not available, for any reason -
    // the caller then falls back to its own picture.
    bool ReadInto(BYTE* pDst, LONG pitch, UINT32 width, UINT32 height)
    {
        if (pDst == nullptr || width == 0 || height == 0) return false;
        if (!EnsureOpen()) return false;

        const uint32_t magic   = Load32(OffMagic);
        const int32_t  srcW    = (int32_t)Load32(OffWidth);
        const int32_t  srcH    = (int32_t)Load32(OffHeight);
        const int32_t  srcSlot = (int32_t)Load32(OffSlotSize);

        // Only accept a buffer that is ours and matches the size we were asked
        // for. Anything else and we would be reinterpreting someone else's bytes.
        if (magic != Magic) return false;
        if (srcW != (int32_t)width || srcH != (int32_t)height) return false;
        if (srcSlot != (int32_t)(width * height * 4)) return false;

        const SIZE_T rowBytes = (SIZE_T)width * 4;

        // A few attempts only. If the writer is mid-frame every time, the app is
        // busy and showing the previous picture is better than stalling a call.
        for (int attempt = 0; attempt < 4; ++attempt)
        {
            const int64_t seqBefore = Load64(OffSequence);
            if (seqBefore & 1) continue;              // odd = write in progress

            const int32_t slot = (int32_t)Load32(OffActiveSlot);
            if (slot != 0 && slot != 1) return false;

            const BYTE* src = m_view + HeaderSize + (SIZE_T)slot * (SIZE_T)srcSlot;

            for (UINT32 r = 0; r < height; ++r)
                memcpy(pDst + (LONG)r * pitch, src + (SIZE_T)r * rowBytes, rowBytes);

            const int64_t seqAfter = Load64(OffSequence);
            if (seqAfter == seqBefore) return true;   // nothing moved: copy is whole
        }
        return false;
    }

    void Close()
    {
        if (m_view)  { UnmapViewOfFile(m_view); m_view = nullptr; }
        if (m_map)   { CloseHandle(m_map);      m_map = nullptr; }
    }

private:
    // Must match SharedFrameBuffer.cs exactly.
    static constexpr uint32_t Magic = 0x43575049;   // 'IPWC'
    static constexpr SIZE_T   HeaderSize = 64;
    static constexpr SIZE_T   OffMagic = 0, OffWidth = 8, OffHeight = 12;
    static constexpr SIZE_T   OffSlotSize = 20, OffSequence = 24, OffActiveSlot = 32;

    bool EnsureOpen()
    {
        if (m_view) return true;

        // Retrying every frame would hammer the system while the app is closed,
        // so back off and check occasionally instead.
        const ULONGLONG now = GetTickCount64();
        if (now - m_lastTry < 1000) return false;
        m_lastTry = now;

        m_map = OpenFileMappingW(FILE_MAP_READ, FALSE, L"Local\\iPhoneWebcamFrames");
        if (m_map == nullptr) return false;

        m_view = (BYTE*)MapViewOfFile(m_map, FILE_MAP_READ, 0, 0, 0);
        if (m_view == nullptr) { CloseHandle(m_map); m_map = nullptr; return false; }
        return true;
    }

    uint32_t Load32(SIZE_T off) const
    {
        return *reinterpret_cast<volatile const uint32_t*>(m_view + off);
    }

    int64_t Load64(SIZE_T off) const
    {
        return *reinterpret_cast<volatile const int64_t*>(m_view + off);
    }

    HANDLE      m_map = nullptr;
    BYTE*       m_view = nullptr;
    ULONGLONG   m_lastTry = 0;
};

#endif
