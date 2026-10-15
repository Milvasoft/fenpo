# Fenpo Proje Planı

> Alınmış kararların tam listesi: [DECISIONS.md](DECISIONS.md).

> **fenestra** (pencere) + **pomum** (elma). iPhone ile Windows arasında, Mac ile iPhone arasındaki uyuma olabildiğince yakın bir deneyim.

## Hedef

Windows kullanan bir iPhone sahibi bilgisayarına oturduğunda telefonuna dokunmak zorunda kalmamalı. Bildirimler, mesajlar, aramalar, pano, dosyalar ve kamera iki cihaz arasında kendiliğinden akmalı. Kurulumdan sonra kullanıcı Fenpo'yu "uygulama" olarak değil, Windows'un bir parçası gibi hissetmeli.

**Kıyas noktası:** Phone Link'in iPhone desteği (bildirim, mesaj, arama) ve kapanan Intel Unison (dosya ve fotoğraf). Fenpo ikisini birleştirip üstüne Mac'te olan ama Windows'ta hiç olmayan özellikleri ekliyor: pano, kamera, ekran yansıtma, yakınlık kilidi, odak eşitleme.

## Mac + iPhone özellikleri ve Fenpo karşılıkları

| Apple özelliği | Fenpo karşılığı | Nasıl | Ne kadar yakın |
|---|---|---|---|
| iPhone bildirimleri Mac'te | Bildirimler | BLE ANCS, iPhone'da uygulama gerekmez | Tam. Kapatma ve olumlu/olumsuz aksiyon var, WhatsApp gibi uygulamalara yazılı yanıt yok |
| Mesajlar (SMS/iMessage) | Mesajlar | Bluetooth MAP | Yüksek. Grup mesajı, medya ve eski geçmiş yok (iOS kısıtı) |
| iPhone aramaları Mac'te | Aramalar ve Ada | Bluetooth HFP (`PhoneLineTransportDevice`) + ANCS | Yüksek. Ses PC'den. Kısıtlı yetenek gerektiriyor, spike şart |
| Kişiler ve arama geçmişi | Rehber, geçmiş | Bluetooth PBAP | Tam |
| Medya kontrolü | Şu an çalıyor + medya tuşları | BLE AMS | Tam |
| Universal Clipboard | Pano | PC→iPhone: Fenpo kanalı. iPhone→PC: Arkaya dokun, Eylem Düğmesi, Denetim Merkezi, paylaşım menüsü | Orta. iOS arka planda panoyu okumaya izin vermiyor; kopyalama tek dokunuşa iniyor |
| AirDrop | Dosya ve fotoğraf gönderimi | Paylaşım uzantısı + Wi‑Fi üzerinden TLS. PC'de LocalSend v2 uyumluluğu | Yüksek |
| Handoff | "PC'de aç" / "iPhone'da aç" | Paylaşım menüsü ve PC'deki komut | Orta. Otomatik değil, tek dokunuş |
| Continuity Camera | Fenpo Kamera | iPhone videoyu HEVC/H.264 ile yollar, Windows'ta sanal kamera (`MFCreateVirtualCamera`) | Yüksek. iPhone'da uygulama ön planda olmalı |
| iPhone Mirroring | Ekran yansıtma + kontrol | ReplayKit yayın uzantısı (veya ayrı GPL UxPlay alıcısı) + PC'nin BLE klavye/fare olması (HID over GATT, AssistiveTouch) | Deneysel ama en "vay be" özellik |
| Instant Hotspot | Hotspot'a bağlan | iPhone'da Kişisel Erişim Noktası açıksa Windows WLAN API ile tek tık bağlanma | Orta. Hotspot uzaktan açılamaz |
| Auto Unlock (Apple Watch) | Yakınlık kilidi + Face ID ile kilit açma | BLE RSSI ile `LockWorkStation`. Kilit açma için özel Credential Provider | Kilitleme tam, kilit açma deneysel |
| Find My (sesi çal) | Telefonu çaldır | Fenpo BLE kanalı uygulamayı uyandırır, bildirim + ses | Orta. Sessiz modda kısıtlı |
| Odak eşitleme | Odak | iOS Focus Filter uzantısı → PC'de gösterge ve Fenpo bildirimlerini susturma | Orta. Windows'ta Rahatsız Etme'yi açmanın genel API'si yok |
| Pil durumu | Pil | BLE Battery Service, uygulama gerekmez | Tam |
| Sidecar, Universal Control | Yok | iOS ve Windows tarafında anlamlı bir yol yok | Kapsam dışı |

Detaylar ve kaynaklar: [RESEARCH.md](RESEARCH.md).

## Yol haritası

Her aşama kendi başına kullanılabilir bir ürün çıkarıyor. Aşama bitmeden bir sonrakine geçilmez.

### Aşama 0: Spike'lar (2–3 hafta)

Planı en çok etkileyen belirsizlikleri, ürün kodu yazmadan önce küçük deneme projeleriyle kapatıyoruz. Her spike `spikes/` altında, bir README ile sonuç ve karar notu bırakır.

| # | Spike | Neyi cevaplıyor | Başarısız olursa |
|---|---|---|---|
| S1 | .NET'ten ANCS + AMS + Battery | Eşleşme akışı (Windows Ayarlar mı, uygulama tetiklemesi mi), `Uncached` servis keşfi, Service Changed. Yakınlık kilidi için RSSI nereden okunur (bağlı GATT bağlantısı, reklam, klasik bağlantı) | Python örneği (`312arjun/iphone-companion`) referans alınır. RSSI okunamazsa yakınlık kilidi bağlantı kopmasına dayanır |
| S2 | MAP ile mesaj okuma ve gönderme | OBEX istemcisi, iPhone tuhaflıkları, gönderme | MyPhone'un `MyPhone.OBEX` kodu temel alınır |
| S3 | HFP arama, PC'den ses | `PhoneLineTransportDevice.RequestAccessAsync` güncel Windows 11'de çalışıyor mu | Aramalar "iPhone'da yanıtla" düzeyinde kalır (ANCS aksiyonları) |
| S4 | iOS uygulaması BLE merkez, PC çevre birim | AccessorySetupKit ile kurulum, arka planda uyanma, zorla kapatma sonrası. Windows tarafı (`GattServiceProvider`, reklam, `rx`/`tx` karakteristikleri) Windows oturumunda S5'ten önce yapılır, iOS testinin karşı ucu olur | Fenpo kanalı sadece uygulama açıkken çalışır |
| S5 | Wi‑Fi üzerinden TLS eşleşme | QR → karşılıklı sertifika sabitleme, DNS-SD keşif | Elle IP girme yedeği |
| S6 | Arka planda `UIPasteboard` yazma | PC→iPhone pano kendiliğinden olabilir mi | PC'den gelen içerik bildirimden "Kopyala" ile alınır |
| S7 | Sanal kamera | `VCamNetSample` ile .NET Native AOT medya kaynağı, MSIX ile kayıt | C++ medya kaynağına geçilir |
| S8 | iPhone Device Information (0x180A) | Model adını uygulamasız okuyabilir miyiz | Model bilgisi Fenpo kanalından gelir |

**Durum:**
- **S1 ve S8 bitti (16 Ekim).** ANCS, AMS, Battery ve Current Time çalışıyor. Eşleşme Windows Ayarlar'dan yapılıyor; yakınlık kilidi reklam RSSI'siyle yapılacak (DECISIONS #28–30). DIS'ten model kimliği okunuyor, iOS sürümü okunmuyor. Yeniden bağlandıktan sonra abonelik yenileme ve canlı bildirim gecikmesi Aşama 1'de doğrulanacak ([README](../spikes/S1-ancs-ams/README.md)).

### Aşama 1: Telefonun masada (MVP)

iPhone'da Fenpo uygulaması olmadan çalışan her şey.

- Windows uygulaması: tepsi ikonu, tepsi paneli, ana pencere iskeleti, ayarlar
- Görünüm (Sistem, Açık, Koyu) ve dil (Sistem dili, Türkçe, English) ayarı, ilk günden. Sonradan eklemek her ekranı yeniden elden geçirmek demek
- Bluetooth eşleşme sihirbazı
- Bildirimler: ANCS → Windows bildirimi, kapatma ve aksiyonlar, uygulama adı ve ikonu (iTunes Lookup + önbellek)
- Medya: şu an çalıyor kartı, klavye medya tuşları (`SystemMediaTransportControls`)
- Pil ve bağlantı durumu
- Yakınlık kilidi (RSSI + `LockWorkStation`)
- Yerel API (named pipe) üzerinden durum yayını, kabuk bar'ı için

**Bitti sayılması için:** Uygulama 8 saat kesintisiz çalışıyor, iPhone uzaklaşıp gelince kendiliğinden yeniden bağlanıyor, bildirim gecikmesi 1 saniyenin altında.

### Aşama 2: Mesajlar ve aramalar

- MAP: konuşma listesi, mesaj okuma, gönderme, okundu işaretleme, bildirimden yanıt
- PBAP: rehber ve arama geçmişi, mesaj ve aramalarda isim çözümleme
- HFP: arama başlatma, gelen aramayı yanıtlama, ses PC'den
- Fenpo Ada: gelen arama ve aktarımlar için ekranın üst ortasındaki pencere

### Aşama 3: iPhone uygulaması ve aktarım

- iOS uygulaması: eşleşme (QR + AccessorySetupKit), ana ekran, ayarlar (görünüm ve dil dahil)
- Fenpo kanalları: BLE kontrol kanalı + Wi‑Fi TLS veri kanalı
- Dosya ve fotoğraf: paylaşım uzantısı, PC'de bırakma alanı, Dosya Gezgini sağ tık komutu, Canlı Etkinlik
- Pano: App Intent'ler, hazır Kısayol, Denetim Merkezi düğmeleri, kilit ekranı widget'ı
- "PC'de aç" ve "iPhone'da aç"
- Telefonu çaldır, PC'yi kilitle
- LocalSend v2.2 uyumluluğu (Windows tarafında)

### Aşama 4: Mac'te olmayanı yap

- Fenpo Kamera (sanal kamera, Wi‑Fi ve USB)
- Dosyalar uygulamasında PC klasörleri (File Provider uzantısı)
- Ekran yansıtma (ReplayKit yayın uzantısı, isteğe bağlı UxPlay)
- Hotspot'a tek tık bağlanma
- Odak eşitleme (iPhone → PC)
- Ekran görüntülerini otomatik aktarma (uygulama uyandığında)

### Aşama 5: Deneysel

- Face ID ile Windows kilidini açma (C++ Credential Provider, ayrı kurulum)
- PC'nin iPhone için klavye ve fare olması (HID over GATT) → yansıtma ile birlikte "iPhone Mirroring"
- Kabuk (kabuk) bar modülü ve üçüncü taraf eklenti SDK'sı

## Kapsam dışı

- macOS ve Linux
- Fenpo sunucusu veya bulut hesabı. Her şey yerel; internet sadece uygulama ikonlarını çekmek için kullanılır.
- iCloud'a bağlanmak, Apple'ın özel protokollerini taklit etmek (Handoff, Universal Clipboard'ın gerçek protokolü). Hem kırılgan hem App Store ve hukuk riski.

## Android iş kolu

Android, iPhone ile paralel ayrı bir oturumda geliştiriliyor (DECISIONS #10, #18). Aşamaları kendi içinde:

- **A. Araştırma ve tasarım (kod yok):** `docs/ANDROID.md` (fizibilite, teknoloji seçimi, en düşük sürüm, kaynaklarıyla) ve `design/android/android.html` (iPhone ekranlarının Material 3 karşılıkları, iki tema). Buğra onaylamadan koda geçilmez.
- **B. İskelet:** Gradle projesi, tema ve diller, CI, tasarıma birebir ekranlar.
- **C. Eşleşme ve kanallar:** QR eşleşme, Fenpo BLE kanalı, Wi‑Fi TLS kanalı.
- **D. Özellikler:** iPhone Aşama 3 ve 4'ün Android karşılıkları; Android'e özel olarak bildirim aynalama (yazılı yanıtla), SMS ve ekran yansıtmada tam kontrol.

## Açık konular

Kararlaşmış her şey [DECISIONS.md](DECISIONS.md) içinde. Açık kalanlar:

1. **Android'in en düşük sürümü:** Android oturumu önerir.
2. **Dağıtım:** Microsoft Store kısıtlı yetenekleri (HFP için `phoneLineTransportManagement`) büyük ihtimalle onaylamaz. Öneri: GitHub Releases üzerinden imzalı MSIX + winget. Store'a sonra, aramasız bir sürümle girilebilir.
