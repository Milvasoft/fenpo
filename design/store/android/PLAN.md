# Fenpo for Android: Google Play mağaza içeriği planı

18 Ekim 2026. Buğra planı ve ikinci taslağı (marka zemini, telefon çerçevesi) onayladı; her şey üretildi. Hiçbir şey
yüklenmez ya da yayınlanmaz. Buğra demeden commit edilmez.

## Klasör

Her şey `design/store/android/` altında ve koddan yeniden üretilebilir. Windows ve iPhone'un mağaza içeriği aynı adlı
dosyalarla çakışmasın diye platform adıyla ayrı duruyor.

```
design/store/android/
├── SPECS.md        ölçüler ve kontrol tarihi (Google'ın sayfaları, 18 Ekim 2026)
├── .gitignore      build/ (video sahneleri, indirilen müzik)
├── PLAN.md         bu belge
├── CHECK.md        script'in yazdığı denetim: ölçüler, alfa, sayılar, dosya boyutları, ffprobe
├── README.md       klasör → içerik → ölçü → Play Console'da nereye yüklenir
├── LICENSES.md     videodaki müziğin kaynağı ve lisansı
├── scripts/
│   ├── specs.json  bütün ölçüler tek yerde
│   └── build.py    tek komut: python design/store/android/scripts/build.py
├── metadata/       tr.md, en.md: başlık ≤ 30, kısa ≤ 80, tam ≤ 4000, öne çıkan grafik alt metni ≤ 140
├── raw/<dil>/<tema>/          uygulamanın ham ekranları, başlıksız (açık ve koyu)
└── play/
    ├── icon-512.png
    ├── feature-graphic-<dil>.png
    ├── phone/<dil>/<tema>/    tasarlanmış ekran görüntüleri (başlık + uygulama)
    ├── video/promo-<dil>.mp4
    └── her klasörde contact.jpg (küçük resim tablosu)
```

`design/screenshots/android` ve `design/logo/store/android` bu yapının yerini alır: ham ekranlar `raw/`'a,
simge ve öne çıkan grafik `play/`'e geçer, eski klasörler silinir. Böylece tek kopya kalır.

## Ekranlar nasıl üretiliyor

Uygulamanın **gerçek Compose ekranları**, ayrı bir taklit arayüz değil. Her modüldeki `StoreScreenshotTest` ekranı
Robolectric'te örnek verilerle çizer (PC adı "Ofis PC"/"Office PC", uydurma dosya, kişi ve şarkı adları). Telefondan
alınmadığı için Buğra'nın kişisel verisi görüntülere giremez. Sistem durum çubuğu Robolectric'te olmadığından,
telefonun demo modundaki gibi temiz bir durum çubuğu çizilir: 12:00, tam pil, Wi‑Fi; bildirim simgesi yok.

Tasarlanmış ekran görüntüsü 1080×1920 (9:16): Fenpo'nun mağaza zemini (Mürekkep'ten Alacakaranlık'a geçiş, sağ üstte
yumuşak bir Çiçek ışığı), üstte görüntünün %17,5'ini kaplayan beyaz bir fayda başlığı, altında uygulama ekranı %80
boyutta koyu çerçeveli bir telefonun içinde. Zemin iki temada aynı, yalnızca ekran temaya uyar. Başlıklar
`metadata/<dil>.md`'den okunur (testler de script de aynı dosyayı kullanır).

## Ekran görüntüleri (7, her dil, açık ve koyu)

| # | Ekran | TR başlık (≤ 28) | EN başlık (≤ 28) |
|---|---|---|---|
| 01 | Ana ekran, PC'ye bağlı | Telefonun ve PC'n bir arada | Your phone and PC, together |
| 02 | PC sekmesi: medya, çaldır, kilitle | PC'yi telefondan yönet | Control your PC from here |
| 03 | Aktarımlar | Dosyaları iki yönde gönder | Send files both ways |
| 04 | Fare ve klavye | Telefon PC'nin faresi olur | Your phone is the PC's mouse |
| 05 | Sunum kumandası | Sunumu cebinden yönet | Run slides from your pocket |
| 06 | Web kamerası (ekran hep koyu) | Telefonun web kameran olsun | Use your phone as a webcam |
| 07 | Ayarlar | Ne paylaşılacağını sen seç | You choose what is shared |

Play'e her dil için **açık tema** seti yüklenir (sayfada tutarlı görünür); koyu set hazır durur.

Kurallar: Play görsellerinde App Store, iPhone ya da başka platform adı ve cihazı yok ("Windows PC" geçebilir).
Fiyat, "ücretsiz", "en iyi", "1 numara" gibi ifadeler yok. Yalnızca gerçek özellikler. Kişisel veri yok.

## Öne çıkan grafik

1024×500, ekran görüntüleriyle aynı mağaza zemini, Çekirdek logosu, "Fenpo" ve tek satır (metin ve alt metin
`metadata/<dil>.md`'de).

## Tanıtım videosu (her dil, 1920×1080, 27 sn)

Play videoyu YouTube bağlantısı olarak alır; dosya üretilir, yükleme notları README'de.

| Saniye | Görüntü | Altyazı (TR / EN) |
|---|---|---|
| 0–4 | Ana ekran, bağlı | PC'ne bağlı, arka planda / Connected to your PC in the background |
| 4–9 | Galeriden "PC'ye gönder", aktarım çubuğu dolar | Fotoğrafı PC'ye gönder / Send a photo to your PC |
| 9–14 | PC sekmesi, müzik kontrolü | PC'deki müziği yönet / Control the PC's music |
| 14–19 | Fare ve klavye | Telefonun PC'nin faresi olsun / Your phone as the PC's mouse |
| 19–24 | Web kamerası | Web kamerası olarak kullan / Use it as a webcam |
| 24–30 | Logo ve "Fenpo for Windows ile çalışır" | Fenpo / Fenpo |

**Karar (Buğra):** video gerçek ekranların örnek verili görüntülerinden yapılır. Her sahne 1920×1080: solda altyazı,
sağda telefonun içinde ekran; sahneler 5 sn ve sabit (yakınlaşma efekti görüntüyü titrettiği için kaldırıldı), bir sahneden ötekine 0,6 sn'lik
bir kayma geçişiyle. İlk sahnelerden itibaren uygulamanın
gerçek ekranları görünür. Sıra ve altyazılar `metadata/<dil>.md`'de (ana ekran, aktarımlar, PC, fare, kamera, kapanış).
Müzik: Mixkit'ten "Digital Clouds" (LICENSES.md); ffmpeg winget ile kuruldu.

## Desteklenmeyen form faktörleri

- **Tablet ve Chromebook:** desteklenmiyor. Arayüz telefon için tasarlandı; tablet ekran görüntüsü yüklenmez.
- **TV:** desteklenmiyor.
- **Wear OS:** desteklenmiyor.

## Kontrol (CHECK.md, script yazar)

Her dosyanın tam ölçüsü; simge dışında alfa yok; simge ≤ 1 MB; ekran görüntüsü sayısı her dilde 2–8 arası; meta veri
uzunlukları sınır içinde; emoji ya da büyük harfle doldurma yok; video için ffprobe çıktısı (1920×1080, H.264, süre).

## Durum

1. Plan ve taslak: onaylandı (ikinci taslak, marka zemini).
2. Ekran görüntüleri, öne çıkan grafik, simge, meta veri, video, kontrol, README: üretildi, `CHECK.md` sorunsuz.
3. Eski `design/screenshots/android` ve `design/logo/store/android` silindi; tek kopya burada.
4. Commit: Buğra deyince.
