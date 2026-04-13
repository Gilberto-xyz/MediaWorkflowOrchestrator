using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace MediaWorkflowOrchestrator.Models
{
    internal static class TrackCleanupOptionVisuals
    {
        public static SolidColorBrush DefaultBackground { get; } = CreateBrush(0x22, 0x14, 0x18, 0x22);
        public static SolidColorBrush DefaultBorder { get; } = CreateBrush(0x70, 0x94, 0xA3, 0xB8);
        public static SolidColorBrush DefaultTitle { get; } = CreateBrush(0xFF, 0xF8, 0xFA, 0xFF);
        public static SolidColorBrush DefaultCaption { get; } = CreateBrush(0xCC, 0xD5, 0xDF, 0xEE);

        public static SolidColorBrush SelectedBackground { get; } = CreateBrush(0xCC, 0x54, 0x2A, 0x8C);
        public static SolidColorBrush SelectedBorder { get; } = CreateBrush(0xFF, 0xD2, 0xA8, 0xFF);

        public static SolidColorBrush PrimaryBackground { get; } = CreateBrush(0xCC, 0x1B, 0x6A, 0x43);
        public static SolidColorBrush PrimaryBorder { get; } = CreateBrush(0xFF, 0x73, 0xF0, 0xA3);

        public static SolidColorBrush Foreground { get; } = CreateBrush(0xFF, 0xFF, 0xFF, 0xFF);
        public static SolidColorBrush CaptionForeground { get; } = CreateBrush(0xE6, 0xEF, 0xFF, 0xF7);

        public static Brush GetBackground(bool isSelected, bool isPrimary) =>
            isPrimary ? PrimaryBackground : isSelected ? SelectedBackground : DefaultBackground;

        public static Brush GetBorder(bool isSelected, bool isPrimary) =>
            isPrimary ? PrimaryBorder : isSelected ? SelectedBorder : DefaultBorder;

        public static Brush GetTitleBrush(bool isSelected, bool isPrimary) =>
            isPrimary || isSelected ? Foreground : DefaultTitle;

        public static Brush GetCaptionBrush(bool isSelected, bool isPrimary) =>
            isPrimary || isSelected ? CaptionForeground : DefaultCaption;

        private static SolidColorBrush CreateBrush(byte a, byte r, byte g, byte b) =>
            new(Color.FromArgb(a, r, g, b));
    }
}
