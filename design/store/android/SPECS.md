# Google Play görsel ve metin ölçüleri

Kontrol tarihi: **18 Ekim 2026**, Google Play Console yardım sayfalarına göre. Bütün sayılar `scripts/specs.json`'da
duruyor; `scripts/build.py` bunlara göre denetler. Ölçüler değişirse önce `specs.json` güncellenir.

| Öğe | Kural | Fenpo |
|---|---|---|
| Uygulama simgesi | 512×512 PNG, 32 bit, en fazla 1 MB; Play köşeleri kendisi yuvarlar | `play/icon-512.png`, tam kare, opak |
| Öne çıkan grafik | 1024×500 JPEG ya da 24 bit PNG, alfa yok | `play/feature-graphic-{tr,en}.png` |
| Öne çıkan grafik alt metni | en fazla 140 karakter | `metadata/<dil>.md` |
| Telefon ekran görüntüsü | JPEG ya da 24 bit PNG, alfa yok; kenarlar 320–3840 px, uzun kenar kısanın en fazla 2 katı; her dil için 2–8 adet (öne çıkmak için en az 4, 9:16 önerilir) | 1080×1920, her dil için 7 |
| Tanıtım videosu | YouTube bağlantısı; herkese açık ya da liste dışı, reklamsız, yaş sınırı yok, yerleştirilebilir | `play/video/promo-{tr,en}.mp4`, 1920×1080, H.264, yaklaşık 27 sn |
| Başlık | en fazla 30 karakter | `metadata/<dil>.md` |
| Kısa açıklama | en fazla 80 karakter | `metadata/<dil>.md` |
| Tam açıklama | en fazla 4000 karakter | `metadata/<dil>.md` |
| Tablet, Chromebook, TV, Wear OS | isteğe bağlı | Desteklenmiyor; yüklenmez |

Play'in metin ve görsel kuralları, `build.py`'nin de denetledikleri: emoji yok; büyük harfle doldurma yok; fiyat,
"ücretsiz", "en iyi", "1 numara" gibi ifadeler yok; başka mağaza ya da platform adı ve cihazı yok ("Windows PC"
geçebilir); yalnızca uygulamada olan özellikler; kişisel veri yok.
