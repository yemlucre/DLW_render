using System;
using System.IO;

namespace Agamemnon.Render;

/// <summary>渲染输出帧：PNG 字节 + 像素尺寸。帧是渲染包的唯一画面交付物。</summary>
public sealed record CanvasFrame(int WidthPx, int HeightPx, byte[] PngBytes)
{
    /// <summary>把帧保存为 PNG 文件（目录不存在时创建）。</summary>
    public string Save(string path)
    {
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full));
        File.WriteAllBytes(full, PngBytes);
        return full;
    }
    /// <summary>是否为合法 PNG（8 字节签名 + IHDR 尺寸与记录一致）。</summary>
    public bool IsPng()
    {
        if (PngBytes == null || PngBytes.Length < 24) return false;
        byte[] signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        for (int i = 0; i < 8; i++) if (PngBytes[i] != signature[i]) return false;
        int w = (PngBytes[16] << 24) | (PngBytes[17] << 16) | (PngBytes[18] << 8) | PngBytes[19];
        int h = (PngBytes[20] << 24) | (PngBytes[21] << 16) | (PngBytes[22] << 8) | PngBytes[23];
        return w == WidthPx && h == HeightPx;
    }
}
