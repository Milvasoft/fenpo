# App Store görselleri: içerik planı

Onay için. Onaydan sonra hepsi koddan üretilir (`scripts/build`), hiçbir şey yüklenmez ya da gönderilmez, sen
söylemeden commit'lenmez. Teknik ölçüler `SPECS.md`'de.

`design/mobile/app-store/` (dünkü 6.9" görseller) bu yapının içine taşınır: kaynak aynı (CI'daki simülatör ekranları
ve `store-frames` düzeni), ama artık burada, iki boyutta ve tek komutla üretiliyor. Eski klasör kalkar.

## Ekran görüntüleri (her dil ve tema için 7, iki boyut)

Her biri: üstte kısa bir başlık ve bir satır, altında telefon çerçevesinde gerçek ekran, Fenpo renklerinde arka plan.
Boyutlar: **iphone-6.3** (1206 × 2622, zorunlu) ve **iphone-6.9** (1320 × 2868). Diller: tr, en. Temalar: açık, koyu
(App Store Connect'e bir dil için tek tema yüklenir; hangisini seçeceğin sana kalmış, öneri: açık).

| # | Ekran | Başlık (tr) | Alt satır (tr) | Headline (en) | Subline (en) |
|---|---|---|---|---|---|
| 01 | Ana ekran | Telefonun ve bilgisayarın tek ekip | Pano, dosya ve kamera tek dokunuşta | Your phone and PC, one team | Clipboard, files and camera in one tap |
| 02 | Aktarımlar | Dosyalar saniyeler içinde | Fotoğraf ve videolar Wi‑Fi ile gider | Files across in seconds | Photos and videos go over Wi‑Fi |
| 03 | Web kamerası | iPhone'un web kameran olsun | Görüntülü görüşmelerde net bir görüntü | Your iPhone is your webcam | A sharp picture for every video call |
| 04 | PC | Bilgisayarın avucunda | Müzik, sunum, fare ve klavye | Your PC in your hand | Music, slides, mouse and keyboard |
| 05 | Fare ve klavye | Telefonun fare ve klavye olsun | Kanepeden bilgisayarı kullan | A touchpad in your pocket | Use your PC from across the room |
| 06 | Sunum kumandası | Sunumu cebinden yönet | İleri, geri ve lazer işaretçi | Present from your pocket | Next, back and a laser pointer |
| 07 | Ayarlar | Sunucu yok, hesap yok | Her şey kendi cihazlarında kalır | No server, no account | Everything stays on your devices |

Durum çubuğu düzeltmesi: dünkü görsellerde pil "şarj oluyor" simgesiyle görünüyordu; yenilerde tam pil, şarj simgesi
yok, 9:41, tam sinyal.

## Yaratıcı varlıklar (dil başına)

| Dosya | Boyut | İçerik |
|---|---|---|
| `creative/<dil>/header-21x9.png` | 3840 × 1646 | Solda Fenpo logosu ve "Telefonun ve bilgisayarın tek ekip" / "Your phone and PC, one team", sağda yan yana üç telefon (Ana ekran, PC, Aktarımlar). Koyu Mürekkep zemin, Çiçek ve Alacakaranlık ışımaları. |
| `creative/<dil>/search-3x2.png` | 3840 × 2560 | Ortada logo ve başlık, altında üç telefon; amaç bir bakışta anlaşılır ("Telefonunla bilgisayarın arasında pano, dosya, kamera"). |
| `creative/<dil>/universal-16x9.png` | 5244 × 2950 | İkisinin yerine kullanılabilir; logo, başlık ve telefonlar 21:9 ve 3:2 kırpımlarının ortak alanında. `check` iki kırpımı da üretip `contact.jpg`'ye koyar. |

Bilgisayar ya da başka bir cihaz çizilmiyor (2.3.10); konu telefonun ekranları.

## Uygulama önizlemesi (dil başına, 886 × 1920, 24–26 sn)

Simülatörde gerçek uygulamanın kaydı: DEBUG'da bir "tanıtım" sahnesi ekranları sırayla gezer (Ana ekran → Aktarımlar →
web kamerası → PC'de şu an çalıyor → Fare ve klavye → Sunum → Ayarlar), her biri ~3.5 sn; `simctl io recordVideo` kaydeder.
Üstüne sessiz izlenebilsin diye her bölümde ekran görüntüsündeki başlık yazılır. Kapak karesi 5. saniye (Ana ekran +
başlık). İki sürüm:

- `preview/<dil>/fenpo-preview.mp4`: müziksiz (sessiz stereo iz, Apple bütün izlerin etkin olmasını istiyor).
- `preview/<dil>/fenpo-preview-music.mp4`: müzikli. Müzik Mixkit'in ücretsiz lisanslı bir parçası olacak; **indirmeden
  önce parçayı sana sorarım** (ad, bağlantı, boyut), lisansı `LICENSES.md`'ye yazılır. Üretilmiş müzik yok.

## Meta veri (`metadata/tr.md`, `metadata/en.md`)

App Store Connect'teki metinlerin dosyası; `check` sınırları denetler: ad ≤ 30, alt başlık ≤ 30, anahtar kelimeler
≤ 100 (virgülle, boşluksuz, adın kelimelerini — "fenpo", "link" — tekrar etmeden), tanıtım ≤ 170, açıklama ≤ 4000, yenilikler,
URL'ler. Kaynak `docs/ios/APP-STORE.md` ve App Store Connect'e girilmiş hali ("Fenpo Link", Windows 11).

## Klasörler

```
design/store/
├── README.md        klasör → içerik → boyut → App Store Connect'te nereye
├── PLAN.md, SPECS.md, specs.json, CHECK.md, LICENSES.md
├── scripts/         build (tek komut), frames, creative, preview, check
├── raw/<dil>/<tema>/NN_ad.png          simülatör ekranları (1320 × 2868)
├── appstore/iphone-6.3/<dil>/<tema>/NN_ad.png
├── appstore/iphone-6.9/<dil>/<tema>/NN_ad.png
├── appstore/creative/<dil>/…
├── appstore/preview/<dil>/…
└── metadata/<dil>.md
```

Her görsel klasöründe bir `contact.jpg` (küçük resimlerin hepsi tek sayfada). `CHECK.md`'yi `check` yazar: tam
ölçüler, alfa yok, dosya boyutları, boyut başına ≤ 10, videoların `ffprobe` çıktısı.

## Nasıl üretilir

Simülatör ekranları ve video Mac gerektirdiği için CI'da: Actions › iOS › Run workflow › **store assets**. İş
`design/store/scripts/build`'i çalıştırır ve bütün `design/store` çıktısını artifact olarak verir; ben indirip buraya
koyarım. Düzen ve kontrol Windows'ta da çalışır (Python, Pillow, ffmpeg).

## Onay istediğim

1. Bu plan (ekranlar, sıra, metinler, yaratıcı varlık düzeni, video akışı).
2. İlk taslak: `draft/01_home-tr-light-6.3.png` (1206 × 2622).
3. Müzikli önizleme için Mixkit'ten parça seçmemi istiyor musun.
