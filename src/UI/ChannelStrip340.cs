using System.Drawing;

namespace Mischpult.UI
{
    /// <summary>3.4: Aufkleber, zweite Farbe auf der Kappe, EQ A/B.</summary>
    internal sealed partial class ChannelStrip
    {
        public static bool EqAbOn;
        public override string StickerOwner => Config.Id;
        protected override Color CapColor2 => Theme.TwoTone && Config.Color2 != 0 ? Color.FromArgb(255, Color.FromArgb(Config.Color2)) : Color.Empty;
    }
}
