# Fenpo for iPhone: App Store görselleri ve metinleri

Fenpo Link'in App Store sayfasında görünen her şey: ekran görüntüleri, yaratıcı varlıklar, uygulama önizlemesi ve
metinler. Hepsi koddan üretilir; elle düzenlenmiş görsel yok. Ölçüler `SPECS.md`'de (Apple kaynaklarıyla), içerik planı
`PLAN.md`'de, son denetimin sonucu `CHECK.md`'de.

## Ne nereye yüklenir

App Store Connect › Uygulamalar › Fenpo Link › iOS Uygulaması › 1.0 › dil seç (Türkçe, English (U.S.)).

| Klasör | İçerik | Ölçü | App Store Connect'te |
|---|---|---|---|
| `appstore/iphone-6.3/<dil>/<tema>/` | 7 ekran görüntüsü, `01`–`07` sırasıyla | 1206 × 2622 | Önizlemeler ve Ekran Görüntüleri › iPhone › **6.3" Display** (zorunlu) |
| `appstore/iphone-6.9/<dil>/<tema>/` | aynı 7 görüntü | 1320 × 2868 | Önizlemeler ve Ekran Görüntüleri › iPhone › **6.9" Display** (isteğe bağlı) |
| `appstore/preview/<dil>/fenpo-preview.mp4` | uygulama önizlemesi, müziksiz (sessiz stereo iz) | 886 × 1920, ~26 sn | aynı yer, ekran görüntülerinin önündeki video yuvası (6.3" ve 6.9") |
| `appstore/preview/<dil>/fenpo-preview-music.mp4` | aynı önizleme, müzikli | 886 × 1920 | yukarıdakinin yerine, ikisinden biri |
| `appstore/preview/<dil>/poster-5s.png` | önizlemenin 5. saniyesi (Ana ekran) | 886 × 1920 | yalnızca bakmak için; Apple kapak karesini videodan seçer |
| `appstore/creative/<dil>/header-21x9.png` | ürün sayfası başlığı | 3840 × 1646 | Yaratıcı Varlıklar › Ürün Sayfası Başlığı (21:9) |
| `appstore/creative/<dil>/search-3x2.png` | arama sonucu görseli | 3840 × 2560 | Yaratıcı Varlıklar › Arama Sonuçları (3:2) |
| `appstore/creative/<dil>/universal-16x9.png` | ikisinin yerine tek görsel | 5244 × 2950 | Yaratıcı Varlıklar › Evrensel (16:9); `contact.jpg`'de 21:9 ve 3:2 kırpımları da var |
| `metadata/<dil>.md` | ad, alt başlık, tanıtım metni, açıklama, anahtar kelimeler, yenilikler, URL'ler | sınırlar `CHECK.md`'de | Uygulama Bilgileri ve 1.0 sürüm sayfası; her `## alan` altındaki metin olduğu gibi yapıştırılır |

Tema: bir dil için ekran görüntülerinin tek temasını yükle (öneri: açık, `light`); ikisini karıştırma. Önizleme açık
temada. Her görsel klasöründe bir `contact.jpg` var: o klasörün bütün görselleri tek sayfada.

Desteklenmeyenler: iPad, Mac, Apple Watch, Vision Pro ve iPhone Duo görselleri yok (Fenpo yalnızca iPhone; Duo için
`SPECS.md`).

## Nasıl üretilir

Tek komut: `scripts/build`. Simülatör ekranları ve önizleme kaydı Mac gerektirdiğinden üretim CI'da:

1. GitHub › Actions › **iOS** › Run workflow › dal `ios`, **App Store assets** işaretli.
2. İş, 6.9" simülatörde uygulamayı açar, `specs.json`'daki 7 ekranı iki dil ve iki temada çeker (`raw/`), tanıtım
   turunu iki dilde kaydeder (`recordings/`), sonra `scripts/build`'i çalıştırır. Müzikli sürüm için parçayı
   Mixkit'ten indirir (`LICENSES.md`).
3. Çıktı `app-store-assets` artifact'i: bu klasörün `appstore/`, `raw/`, `recordings/` ve `CHECK.md` içeriği.

`raw/` ve `recordings/` elindeyse düzen ve denetim her yerde çalışır (Python 3, Pillow, ffmpeg):

```bash
design/store/scripts/build
```

| Betik | İş |
|---|---|
| `scripts/build` | giriş noktası; `--capture <udid> <app>` önce simülatörden çeker, `--skip-preview` videoyu atlar |
| `scripts/capture.sh` | simülatör: durum çubuğu (9:41, tam pil, tam sinyal), ekranlar, önizleme kaydı |
| `scripts/frames.py` | bir ekranı başlık, alt satır ve telefon çerçevesiyle bir görsele çevirir (her boyutta) |
| `scripts/build.py` | ekran görüntüleri, yaratıcı varlıklar, önizleme (ekran değişimlerini kayıttan bulur), `contact.jpg` |
| `scripts/check.py` | ölçüler, alfa, sayılar, `ffprobe` değerleri, metin sınırları, yasak kelimeler → `CHECK.md` |

Metinler: ekran başlıkları `copy.json`, mağaza metinleri `metadata/`. Ölçüler: `specs.json`. Ekranlardaki içerik
uygulamanın DEBUG demo verisi (`FenpoCore/Session/SampleSession.swift`); tanıtım turu
`ios/Fenpo/App/ScreenshotScene.swift` (`-fenpo.screenshot demo`).
