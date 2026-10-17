# Microsoft Store: Fenpo for Windows

Dağıtım kararı: DECISIONS #41 (Store + GitHub Releases). Paket: `tools/Build-Release.ps1 -Channel Store`.

## Store kimliği (Partner Center, 17 Ekim)

| Alan | Değer |
|---|---|
| Store ID | `9MTTKJ8BV1LG` (<https://apps.microsoft.com/detail/9MTTKJ8BV1LG>) |
| Package/Identity/Name | `Milvasoft.Fenpo` |
| Package/Identity/Publisher | `CN=934D5176-C6E5-455B-9AAC-3ECA3C954CB4` |
| PublisherDisplayName | `Milvasoft` |
| Package Family Name | `Milvasoft.Fenpo_tzmzhd3z3x8ea` |

Paket: `tools/Build-Release.ps1 -Channel Store -Version 1.0.0.0 -IdentityName Milvasoft.Fenpo -Publisher "CN=934D5176-C6E5-455B-9AAC-3ECA3C954CB4"`

## Partner Center'da Buğra'nın yapacakları

1. Partner Center'da geliştirici hesabı (bireysel hesap ücretsiz; şirket hesabı Milvasoft adına).
2. Uygulama adını ayır: **Fenpo**. Sonra *Ürün kimliği* sayfasındaki üç değeri bana ver (gizli değiller):
   `Package/Identity/Name`, `Package/Identity/Publisher` (`CN=…`), `Package/Properties/PublisherDisplayName`.
3. Ben paketi bu kimlikle derlerim: `tools/Build-Release.ps1 -Channel Store -Version 0.2.0.0 -IdentityName … -Publisher "CN=…"`.
   Çıkan `artifacts/Store/Fenpo_0.2.0.0_Store.msixbundle` dosyası Partner Center'a yüklenir.
4. Kısıtlı yetenek gerekçeleri ve sertifikasyon notları aşağıda; formlara kopyalanır.

## Mağaza metni

### Türkçe

**Ad:** Fenpo

**Kısa açıklama:** Telefonun ve Windows aynı masada. Bildirimler, mesajlar, aramalar, pano, dosyalar, kamera ve ekran; bulut yok, hesap yok.

**Açıklama:**

Fenpo, iPhone'unu ya da Android telefonunu Windows bilgisayarınla tek bir cihaz gibi kullanmanı sağlar.

- Telefon bildirimleri Windows bildirimi olarak gelir; yanıtla, kapat.
- Mesajlarını bilgisayardan oku ve gönder; gelen aramayı bilgisayardan yanıtla ya da reddet.
- Panoyu iki yönde paylaş: telefonda kopyala, bilgisayarda yapıştır.
- Dosya ve fotoğrafları iki yönde gönder; Dosya Gezgini'nde sağ tıkla "Fenpo ile telefona gönder". LocalSend uyumlu.
- Telefonunu web kamerası yap: Fenpo Kamera, Teams, Zoom ve tarayıcıda kamera olarak görünür.
- Telefonunun ekranını bilgisayarda gör; Android'i fareyle kontrol et, iPhone'a bilgisayarın klavyesini ve faresini ver.
- Telefonu dokunmatik yüzey, klavye ve sunum kumandası olarak kullan.
- Bilgisayarın klasörlerini telefonun dosya uygulamasında aç, telefonun fotoğraflarına bilgisayardan göz at.
- Telefon uzaklaşınca bilgisayar kilitlensin; telefonu bilgisayardan çaldır; hotspot'a tek tıkla bağlan.

Bulut yok, hesap yok, reklam yok. Her şey Bluetooth ve yerel ağ üzerinden, cihazların arasında şifreli gider.

Telefonda ücretsiz Fenpo uygulaması gerekir (App Store ve Google Play). Bildirimler, mesajlar ve aramalar iPhone'da
uygulama olmadan da çalışır.

**Anahtar kelimeler:** telefon, iPhone, Android, bildirim, SMS, pano, dosya aktarımı, web kamerası, ekran yansıtma

### English

**Name:** Fenpo

**Short description:** Your phone and Windows, at the same desk. Notifications, messages, calls, clipboard, files, camera and screen; no cloud, no account.

**Description:**

Fenpo makes your iPhone or Android phone and your Windows PC work as one.

- Phone notifications appear as Windows notifications; reply or dismiss them.
- Read and send messages from the PC; answer or decline incoming calls.
- Share the clipboard both ways: copy on the phone, paste on the PC.
- Send files and photos both ways; right-click in File Explorer and choose "Send to phone with Fenpo". Works with LocalSend.
- Use your phone as a webcam: Fenpo Kamera shows up as a camera in Teams, Zoom and the browser.
- See your phone's screen on the PC; control Android with the mouse, give the iPhone your PC's keyboard and mouse.
- Use your phone as a touchpad, keyboard and presentation remote.
- Open your PC's folders in the phone's files app and browse your phone's photos from the PC.
- Lock the PC when the phone walks away, ring the phone from the PC, join its hotspot with one click.

No cloud, no account, no ads. Everything goes encrypted between your devices over Bluetooth and your local network.

Needs the free Fenpo app on the phone (App Store and Google Play). Notifications, messages and calls from an iPhone work
without the app too.

**Keywords:** phone, iPhone, Android, notifications, SMS, clipboard, file transfer, webcam, screen mirroring

## Gerekli bağlantılar

- Gizlilik politikası: <https://github.com/Milvasoft/fenpo/blob/main/docs/PRIVACY.md>
- Destek: <https://github.com/Milvasoft/fenpo/issues>
- Web sitesi: <https://github.com/Milvasoft/fenpo>

## Kategori ve yaş

- Kategori: Verimlilik (Productivity); ikinci: Araçlar ve yardımcı programlar.
- Yaş derecelendirmesi anketi: kullanıcılar arası iletişim yok (Fenpo kullanıcının kendi cihazları arasında çalışır), satın alma yok, konum paylaşımı yok.

## Kısıtlı yetenek gerekçesi (runFullTrust)

> Fenpo is a desktop companion for the user's phone. It needs full trust for: Bluetooth Low Energy and classic
> Bluetooth profiles (Apple Notification Center Service, Message Access Profile), a local TLS server that the paired
> phone connects to on the local network, a File Explorer context menu command (packaged COM), low-level keyboard and
> mouse hooks while the user hands the PC's keyboard and mouse to the phone (Ctrl+Alt+I, shown with an on-screen
> banner), and installing the optional "Fenpo Kamera" virtual camera.

## Sertifikasyon notları (testçiler için)

> Fenpo needs a phone to show its features: an iPhone (paired in Windows Bluetooth settings; notifications, messages
> and calls work without any app) or the Fenpo app on iPhone/Android (App Store, Google Play), paired by scanning the
> QR code in Settings › Add phone. Without a phone the app opens to its pairing screen and every page explains what it
> needs. A demo video: <VIDEO LINK>.
>
> Fenpo Kamera (optional, Camera page › Install): Windows' virtual camera API (MFCreateVirtualCamera) is served by the
> Frame Server service, which loads the camera's media source only from a machine-wide COM registration; it does not
> see COM classes registered by an MSIX package. So when the user presses Install, Fenpo asks for administrator
> approval (UAC) once, copies the media source DLL to %ProgramFiles%\Fenpo\Camera and registers it with regsvr32.
> Remove on the same page undoes both. Nothing else is installed outside the package.
>
> Fenpo uses the internet only to download app icons from Apple's public iTunes Lookup API. No account, no server,
> no analytics.

## Ekran görüntüleri

Store en az 1, en çok 10 ekran görüntüsü ister (1366×768 ya da daha büyük, 16:9). Herkese açık olacakları için gerçek
kişisel veri olmamalı: isimler, numaralar, mesaj içerikleri, bilgisayar adı. Önerilen sıra: Ana sayfa (bağlı telefon),
Bildirimler, Mesajlar, Dosyalar, Kamera, Ekran yansıtma, Ayarlar; açık ve koyu tema karışık, Türkçe ve İngilizce
listelerde kendi dilinde. Gösterim verisiyle çekilecek (Buğra'nın onayıyla).

## Açık işler

- ARM64 paketi: Native AOT DLL'leri (Fenpo Kamera, Dosya Gezgini komutu) için Visual Studio'da "MSVC ARM64 derleme
  araçları" bileşeni gerekiyor. Yoksa paket yalnızca x64 olur; ARM64 bilgisayarlarda Fenpo öykünmeyle çalışır ama
  Fenpo Kamera ve sağ tık menüsü çalışmaz.
- Demo videosu.
