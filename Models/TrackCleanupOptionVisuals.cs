using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace MediaWorkflowOrchestrator.Models
{
    internal static class TrackCleanupOptionVisuals
    {
        public static SolidColorBrush DefaultBackground { get; } = CreateBrush(0x30, 0x1B, 0x20, 0x2A);
        public static SolidColorBrush DefaultBorder { get; } = CreateBrush(0x78, 0x90, 0x9D, 0xAE);
        public static SolidColorBrush DefaultTitle { get; } = CreateBrush(0xFF, 0xF8, 0xFA, 0xFF);
        public static SolidColorBrush DefaultCaption { get; } = CreateBrush(0xCC, 0xD5, 0xDF, 0xEE);

        public static SolidColorBrush SelectedBackground { get; } = CreateBrush(0xC4, 0x59, 0x3A, 0x16);
        public static SolidColorBrush SelectedBorder { get; } = CreateBrush(0xFF, 0xE5, 0xA3, 0x4F);

        public static SolidColorBrush PrimaryBackground { get; } = CreateBrush(0xC8, 0x1D, 0x63, 0x44);
        public static SolidColorBrush PrimaryBorder { get; } = CreateBrush(0xFF, 0x6B, 0xD2, 0x96);

        public static SolidColorBrush Foreground { get; } = CreateBrush(0xFF, 0xFF, 0xFF, 0xFF);
        public static SolidColorBrush CaptionForeground { get; } = CreateBrush(0xE6, 0xEF, 0xFF, 0xF7);
        public static SolidColorBrush SelectorBackground { get; } = CreateBrush(0xFF, 0xB7, 0xA1, 0xFF);
        public static SolidColorBrush SelectorBorder { get; } = CreateBrush(0xFF, 0xC9, 0xBA, 0xFF);
        public static SolidColorBrush SelectorGlyph { get; } = CreateBrush(0xFF, 0x3C, 0x1D, 0x68);
        public static SolidColorBrush SelectorMutedBackground { get; } = CreateBrush(0x22, 0xFF, 0xFF, 0xFF);
        public static SolidColorBrush SelectorMutedBorder { get; } = CreateBrush(0x70, 0x8A, 0x93, 0xA2);
        public static SolidColorBrush SelectorMutedGlyph { get; } = CreateBrush(0x00, 0xFF, 0xFF, 0xFF);

        public static Brush GetBackground(bool isSelected, bool isPrimary) =>
            isPrimary ? PrimaryBackground : isSelected ? SelectedBackground : DefaultBackground;

        public static Brush GetBorder(bool isSelected, bool isPrimary) =>
            isPrimary ? PrimaryBorder : isSelected ? SelectedBorder : DefaultBorder;

        public static Brush GetTitleBrush(bool isSelected, bool isPrimary) =>
            isPrimary || isSelected ? Foreground : DefaultTitle;

        public static Brush GetCaptionBrush(bool isSelected, bool isPrimary) =>
            isPrimary || isSelected ? CaptionForeground : DefaultCaption;

        public static Brush GetSelectorBackground(bool isSelected) =>
            isSelected ? SelectorBackground : SelectorMutedBackground;

        public static Brush GetSelectorBorder(bool isSelected) =>
            isSelected ? SelectorBorder : SelectorMutedBorder;

        public static Brush GetSelectorGlyph(bool isSelected) =>
            isSelected ? SelectorGlyph : SelectorMutedGlyph;

        private static SolidColorBrush CreateBrush(byte a, byte r, byte g, byte b) =>
            new(Color.FromArgb(a, r, g, b));
    }
}
