using FeedRaider.Console;

namespace FeedRaider.Tests;

public class FrameImageTests
{
    private const int CellW = 8, CellH = 16;

    private static uint Pixel(byte[] px, int w, int x, int y)
    {
        int o = (y * w + x) * 3;
        return (uint)(px[o] << 16 | px[o + 1] << 8 | px[o + 2]);
    }

    [Fact]
    public void A_space_cell_is_all_background()
    {
        var px = new byte[CellW * CellH * 3];
        FrameImage.DrawCell(px, CellW, 0, 0, ' ', 0xff0000, 0x0000ff);
        for (int y = 0; y < CellH; y++)
            for (int x = 0; x < CellW; x++) Assert.Equal(0x0000ffu, Pixel(px, CellW, x, y));
    }

    [Fact]
    public void A_quadrant_cell_paints_foreground_only_in_its_lit_quadrants()
    {
        var px = new byte[CellW * CellH * 3];
        FrameImage.DrawCell(px, CellW, 0, 0, '▘', 0xff0000, 0x0000ff);   // upper-left quadrant only
        Assert.Equal(0xff0000u, Pixel(px, CellW, 0, 0));
        Assert.Equal(0xff0000u, Pixel(px, CellW, CellW / 2 - 1, CellH / 2 - 1));
        Assert.Equal(0x0000ffu, Pixel(px, CellW, CellW / 2, 0));
        Assert.Equal(0x0000ffu, Pixel(px, CellW, 0, CellH / 2));
        Assert.Equal(0x0000ffu, Pixel(px, CellW, CellW - 1, CellH - 1));
    }

    [Fact]
    public void A_cell_lands_at_its_own_column_and_row_of_the_image()
    {
        int w = 3 * CellW;
        var px = new byte[w * 2 * CellH * 3];
        FrameImage.DrawCell(px, w, 2, 1, '█', 0x112233, 0x000000);   // full block at column 2, row 1
        Assert.Equal(0x112233u, Pixel(px, w, 2 * CellW, CellH));
        Assert.Equal(0x112233u, Pixel(px, w, 3 * CellW - 1, 2 * CellH - 1));
        Assert.Equal(0u, Pixel(px, w, 2 * CellW - 1, CellH));
        Assert.Equal(0u, Pixel(px, w, 2 * CellW, CellH - 1));
    }

    [Fact]
    public void A_letter_cell_has_both_ink_and_paper()
    {
        var px = new byte[CellW * CellH * 3];
        FrameImage.DrawCell(px, CellW, 0, 0, 'A', 0xffffff, 0x000000);
        var seen = new HashSet<uint>();
        for (int y = 0; y < CellH; y++)
            for (int x = 0; x < CellW; x++) seen.Add(Pixel(px, CellW, x, y));
        Assert.Equal(new HashSet<uint> { 0xffffffu, 0u }, seen);
    }
}
