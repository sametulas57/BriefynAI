namespace MeetingWeb.Models
{
    // The icons a title may use: general Bootstrap Icons names (stored) with the Turkish name shown in the picker. Picked
    // freely, not tied to a profession. Only these are stored, so a value can be put into a class attribute safely.
    public static class TitleIcons
    {
        public static readonly IReadOnlyList<(string Icon, string Label)> All = new[]
        {
            ("laptop", "Bilgisayar"), ("cpu", "İşlemci"), ("code-slash", "Kod"), ("database", "Veri"), ("phone", "Telefon"),
            ("headset", "Kulaklık"), ("building", "Bina"), ("house", "Ev"), ("shop", "Mağaza"), ("bank", "Banka"),
            ("hammer", "Çekiç"), ("tools", "Aletler"), ("wrench", "Anahtar"), ("truck", "Kamyon"), ("box-seam", "Koli"),
            ("heart-pulse", "Nabız"), ("capsule", "İlaç"), ("hospital", "Hastane"), ("clipboard2-pulse", "Tıbbi kayıt"), ("file-earmark-text", "Belge"),
            ("briefcase", "Çanta"), ("graph-up", "Grafik"), ("calculator", "Hesap makinesi"), ("cash-coin", "Para"), ("pencil", "Kalem"),
            ("brush", "Fırça"), ("palette", "Palet"), ("camera", "Kamera"), ("megaphone", "Megafon"), ("chat-dots", "Sohbet"),
            ("people", "İnsanlar"), ("person-badge", "Kimlik kartı"), ("mortarboard", "Kep"), ("book", "Kitap"), ("globe", "Dünya"),
            ("shield-check", "Kalkan"), ("lightning", "Şimşek"), ("gear", "Dişli"), ("star", "Yıldız"), ("flag", "Bayrak")
        };

        public static bool IsAllowed(string? icon) => icon == null || All.Any(i => i.Icon == icon);
    }
}
