# Fenpo for Android: Google Play mağaza içeriği

Her şey koddan üretilir. Depo kökünden tek komut:

```
python design/store/android/scripts/build.py
```

Bu komut uygulamanın gerçek Compose ekranlarını Robolectric'te örnek verilerle çizer, Play'in kabul etmediği alfa
kanalını kaldırır, metinleri denetler, videoyu kurar ve `CHECK.md`'yi yazar. Görseller değişmediyse
`--skip-render`, video gerekmiyorsa `--skip-video` kullanılır. Gerekenler: JDK 21 ve Android SDK (uygulamayı
derleyenler), Python 3 ile Pillow, ffmpeg (`winget install Gyan.FFmpeg`). Nasıl tasarlandığı `PLAN.md`'de, ölçüler
`SPECS.md`'de.

## Ne nereye yüklenir

Play Console > Fenpo > **Büyüme > Mağaza varlığı > Ana mağaza girişi**; dil seçiciden önce Türkçe (tr-TR),
sonra İngilizce (en-US).

| Klasör / dosya | İçerik | Ölçü | Play Console'da |
|---|---|---|---|
| `metadata/<dil>.md` | Başlık, kısa ve tam açıklama | ≤ 30 / ≤ 80 / ≤ 4000 karakter | Uygulama ayrıntıları |
| `metadata/<dil>.md` | Öne çıkan grafik alt metni | ≤ 140 karakter | Grafikler > Öne çıkan grafik > Alt metin |
| `play/icon-512.png` | Uygulama simgesi | 512×512, 32 bit, opak | Grafikler > Uygulama simgesi (tek, dilden bağımsız) |
| `play/feature-graphic-<dil>.png` | Öne çıkan grafik | 1024×500, 24 bit | Grafikler > Öne çıkan grafik |
| `play/phone/<dil>/light/` | 7 telefon ekran görüntüsü, başlıklı (**yüklenecek set**) | 1080×1920, 24 bit | Grafikler > Telefon ekran görüntüleri, 01'den 07'ye sırayla |
| `play/phone/<dil>/dark/` | Aynı 7 ekran, koyu tema | 1080×1920 | Yedek; istenirse açık set yerine |
| `play/video/promo-<dil>.mp4` | Tanıtım videosu | 1920×1080, H.264, yaklaşık 27 sn | Önce YouTube'a, sonra bağlantısı Video alanına |
| `raw/<dil>/<tema>/` | Başlıksız ham ekranlar | 1080×1920 | Yüklenmez; kaynak |
| her klasörde `contact.jpg` | Küçük resim tablosu | — | Yüklenmez; göz atmak için |
| `CHECK.md` | Ölçü, alfa, sayı ve video denetimi | — | Yüklenmez |
| `build/` | Video sahneleri ve indirilen müzik | — | Depoda değil (`.gitignore`) |

Tablet, Chromebook, TV ve Wear OS için görsel yok: Fenpo bunları desteklemiyor.

## Videoyu YouTube'a yükleme

1. Fenpo'nun (Milvasoft) YouTube kanalına `promo-tr.mp4` ve `promo-en.mp4`'ü ayrı videolar olarak yükle.
2. Görünürlük **Herkese açık** ya da **Liste dışı** (gizli olmaz; Play oynatamaz).
3. **Çocuklar için yapılmadı**; yaş sınırı yok.
4. Para kazanma ve reklamlar **kapalı** (Play reklamlı videoyu kabul etmez).
5. Gelişmiş ayarlarda **Yerleştirmeye izin ver** açık.
6. Müzik Mixkit'ten (bkz. `LICENSES.md`). Content ID itirazı gelirse lisans sayfası kanıt olarak gösterilir.
7. Video bağlantısını Play Console'da o dilin mağaza girişindeki **Tanıtım videosu** alanına yapıştır.

Hiçbir şey kendiliğinden yüklenmez ya da yayınlanmaz; yukarıdakileri Buğra yapar.
