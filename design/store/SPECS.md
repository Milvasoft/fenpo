# App Store teknik ölçüleri

Apple'ın sayfalarından **18 Ekim 2026**'da kontrol edildi:

- Ekran görüntüleri: https://developer.apple.com/help/app-store-connect/reference/app-information/screenshot-specifications
- Uygulama önizlemesi: https://developer.apple.com/help/app-store-connect/reference/app-information/app-preview-specifications
- Yaratıcı varlıklar: https://developer.apple.com/help/app-store-connect/reference/app-information/creative-assets-specifications
- En iyi uygulamalar: https://developer.apple.com/app-store/asset-best-practices/
- Metinler: https://developer.apple.com/help/app-store-connect/reference/app-information/platform-version-information

Ölçülerin makinede okunan tek kaynağı `specs.json`; betikler oradan okur, `check` oradan denetler.

## Ekran görüntüleri (iPhone, dikey)

| Grup | Fenpo'nun kullandığı | Apple'ın kabul ettikleri | Durum |
|---|---|---|---|
| Dinamik Ada, orta ekran (iPhone 14 Pro–18 Pro, 15/16/17) | **1206 × 2622** | 1206 × 2622, 1179 × 2556 | **Zorunlu**: "At least one screenshot for iPhone with Dynamic Island (medium display)" |
| Dinamik Ada, büyük ekran (Pro Max, Plus, Air) | **1320 × 2868** | 1320 × 2868, 1290 × 2796, 1260 × 2736 | İsteğe bağlı; yoksa diğerlerinden ölçeklenir |
| Diğer iPhone boyutları | yok | | Orta ekrandan otomatik ölçeklenir |
| iPhone Duo | yok | 1398 × 2034 (dış), 2007 × 2853 (iç) | Fenpo desteklemiyor. Nisan 2027'den sonra iOS 27.1 SDK ile derlenen uygulamalar için zorunlu; Fenpo şu an Xcode 26.6 (iOS 26 SDK) ile derleniyor |
| iPad, Mac, Apple TV, Watch, Vision Pro | yok | | Fenpo yalnızca iPhone (`TARGETED_DEVICE_FAMILY = 1`) |

Her boyut ve dil için 1–10 görüntü; .png ya da .jpg; **alfa kanalı yok**.

## Uygulama önizlemesi (isteğe bağlı)

| | |
|---|---|
| Çözünürlük | **886 × 1920** dikey (Dinamik Ada büyük ve orta ekran için kabul edilen) |
| Süre | 15–30 sn |
| Kare hızı | en fazla 30 fps |
| Video | H.264, progressive, High Profile ≤ Level 4.0, hedef 10–12 Mbps |
| Ses | stereo, 256 kbps AAC, 44.1 ya da 48 kHz; bütün izler etkin (müziksiz sürümde de sessiz bir stereo iz) |
| Dosya | .mov, .m4v, .mp4; en fazla 500 MB |
| Sayı | dil ve boyut başına en fazla 3 |
| Kapak karesi | varsayılan 5. saniye |

Önizlemeler ürün sayfasında ekran görüntülerinden önce gösterilir.

## Yaratıcı varlıklar (iOS 27 App Store'u, isteğe bağlı)

| Yer | Oran | Çözünürlük | Uzantı |
|---|---|---|---|
| Ürün sayfası başlığı | 21:9 | **3840 × 1646** | .png, .jpg |
| Arama sonuçları | 3:2 | **3840 × 2560** (en az 1920 × 1280) | .png, .jpg |
| Evrensel (ikisinin yerine) | 16:9 | **5244 × 2950** | yalnızca .png |

Alfa kanalı yok. Asıl konu ve yazı ortada durur; evrensel görsel 21:9 ve 3:2'ye kırpılınca da okunur.

## Metinler (`metadata/<dil>.md`)

| Alan | Sınır |
|---|---|
| Ad, alt başlık | 30 karakter |
| Tanıtım metni | 170 karakter |
| Açıklama, bu sürümdeki yenilikler | 4000 karakter |
| Anahtar kelimeler | **100 bayt** (UTF-8; ı, ş, ğ, ü, ö, ç iki bayt), virgülle ayrılır, her biri ikiden uzun; ad ve şirket adı tekrar edilmez, başka uygulama ya da şirket adı yok |

## İçerik kuralları

App Review Guidelines 2.3 (doğru meta veri) ve 2.3.10 (başka platformların adı, logosu, cihazı yok; "Windows PC"
bir bağlantı hedefi olarak geçebilir). Fiyat, "ücretsiz", "en iyi", "#1", ödül, web adresi, telif işareti yok; yalnızca
gerçek özellikler; kişisel veri yok (PC adı "Ofis PC" / "Office PC", içerik uydurma). Durum çubuğu 9:41, tam pil
(şarj simgesi yok), tam sinyal.
