# Windows cihaz testleri

Telefonla denenmesi gerekenler. Her satırda yer, adım ve beklenen sonuç var. Sonucu "Durum" sütununa tarihle yaz; denenmemiş satır "—" kalır. Bluetooth ve Wi‑Fi davranışı emülatörde doğrulanamaz.

Kayıtlar: `%LOCALAPPDATA%\Packages\Milvasoft.Fenpo_*\LocalState\logs\`. Fenpo Kamera'nın hata kaydı: `%ProgramData%\Fenpo\Camera\camera.log`.

## Bağlantı ve eşleşme

| # | Yer | Adım | Beklenen | Durum |
|---|---|---|---|---|
| W1 | Ayarlar › Cihaz › Telefon ekle | **QR koduyla bağla**, telefonda Fenpo › Kodu okut | Telefon bağlanır, Telefonum'da "Bağlı" | — |
| W2 | Ayarlar › Cihaz | iPhone satırında **Eşleşmeyi kaldır** › Kaldır | Fenpo iPhone'u unutur, Windows Bluetooth listesinden de kalkar; Telefonum "Eşleşmedi" | — |
| W3 | Telefonum, Bildirimler | iPhone'da Ayarlar › Bluetooth › PC (i) › Sistem Bildirimlerini Paylaş kapalı | Üstte "iPhone bu bilgisayarla bildirim paylaşmıyor" uyarısı; anahtar açılınca 15 sn içinde kaybolur, bildirimler gelir | — |

## Aramalar

| # | Yer | Adım | Beklenen | Durum |
|---|---|---|---|---|
| A1 | — | Başka telefondan iPhone'u ara | Ada büyüyerek açılır, yeşil halka yanıp söner, bilgisayar Windows'un arama sesini çalar | — |
| A2 | Ada | Kırmızı düğme | Zil susar, arama telefonda reddedilir | — |
| A3 | — | Aramayı telefondan aç | Zil susar, ada kapanır | — |
| A4 | Ayarlar › Özellikler | "Aramada bilgisayar da çalsın" kapalı, tekrar ara | Ada çıkar, ses yok | — |

## Kamera ve ekran

| # | Yer | Adım | Beklenen | Durum |
|---|---|---|---|---|
| K1 | Kamera | **Önizlemeyi başlat**, 2 dakika bekle | Kesintisiz, gecikme yaklaşık 0,2 sn; kayıtta "packets waiting" 0–2 | — |
| K2 | Kamera › Uygulamalarda Fenpo Kamera | **Fenpo Kamera'yı kur**, yönetici onayını ver | Metin "Uygulamanın kamera ayarında Fenpo Kamera'yı seç…" olur | — |
| K3 | Windows Kamera uygulaması veya Teams | Kamera olarak **Fenpo Kamera**'yı seç | Telefonun kamerası kendiliğinden başlar, görüntü uygulamada; Fenpo'nun Kamera sayfasında "Bir uygulama kullanıyor" | — |
| K4 | Teams | Kamerayı kapat | Birkaç saniye içinde telefonun kamerası durur | — |
| K5 | Kamera › Uygulamalarda Fenpo Kamera | **Kaldır**, onay ver | Fenpo Kamera uygulamaların listesinden kalkar | — |
| E1 | Telefonum › Ekranı yansıt (Android) | Telefonda izin ver, YouTube'da bir video aç | Görüntü ve ses bilgisayarda | — |
| E2 | iPhone'da Fenpo › PC › Ekranı PC'de göster | Yayını başlat | Bilgisayarda yansıtma penceresi kendiliğinden açılır; sadece Durdur düğmesi var | — |

## Dosyalar ve paylaşım

| # | Yer | Adım | Beklenen | Durum |
|---|---|---|---|---|
| D1 | Dosya Gezgini | Bir dosyaya sağ tık › **Fenpo ile telefona gönder** | Fenpo Dosyalar sayfasında açılır, dosya telefona gider | — |
| D2 | Dosyalar › Son fotoğraflar | Sayfayı aç | Telefonun son fotoğrafları küçük resimleriyle görünür | — |
| D3 | Dosyalar › Son fotoğraflar | Bir fotoğrafa tıkla | Gelen dosyalar klasörüne iner ve açılır | — |
| D4 | Dosyalar › Son fotoğraflar | Bir fotoğrafı masaüstüne sürükle | Asıl dosya masaüstüne gelir | — |
| D5 | Ayarlar › Telefonla paylaşım | **Klasör ekle** ile bir klasör seç; telefonda Dosyalar (iPhone) veya dosya seçici (Android) | Klasör ve içi görünür, dosya açılır, telefondan yüklenen dosya klasöre düşer | — |
| D6 | Ayarlar › Telefonla paylaşım | "PC bildirimlerini telefona gönder" aç, Windows izni ver, Uygulamaları seç'ten bir uygulama işaretle | O uygulamanın bildirimi telefonda da görünür, PC'de kapatınca telefondan da kalkar | — |
| D7 | Başka bir telefonda LocalSend | Listeden bu bilgisayarı seç, dosya gönder | PC'de "… sana dosya göndermek istiyor" bildirimi; Kabul et deyince dosya Gelen dosyalar'a iner; Reddet deyince gönderim durur | — |

## Hotspot

| # | Yer | Adım | Beklenen | Durum |
|---|---|---|---|---|
| H1 | Telefonum › Hotspot'a bağlan | Telefonda hotspot bilgisi yokken | "Telefonda hotspot adı ve şifresi kayıtlı değil…" uyarısı | — |
| H2 | Telefonda Fenpo › Ayarlar › Hotspot (iPhone'da Kişisel Erişim Noktası) | Ad ve şifreyi gir, hotspot'u aç, PC'de **Hotspot'a bağlan** | 1 dakika içinde bilgisayar hotspot'a bağlanır, düğme "Hotspot'a bağlı" | — |
| H3 | Tepsi paneli | Hotspot düğmesi | H2 ile aynı | — |
