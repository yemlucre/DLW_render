namespace Agamemnon.Drawing;

/// <summary>导入文件通用上限：单个 SVG / 坐标路径文件最大 5 MB。</summary>
public static class DesignImportLimits
{
    public const int MaxFileBytes = 5 * 1024 * 1024;
}
