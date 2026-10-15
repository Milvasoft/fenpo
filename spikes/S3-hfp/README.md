# S3: HFP ile arama, PC'den ses

## Hedef

`PhoneLineTransportDevice` ile iPhone'un HFP rolünü almak, bir `PhoneLine` elde etmek ve aramayı PC'den başlatıp yanıtlamak; sesi PC'nin hoparlörü ve mikrofonundan geçirmek. RESEARCH §4'teki "`RequestAccessAsync` güncel Windows 11'de çalışıyor mu" sorusunu cevaplamak.

## Yapılanlar

- `HfpSpike/`: konsol uygulaması. `Package/AppxManifest.xml` ile paketlenir: `uap10:Subsystem="console"`, `FenpoS3` çalıştırma takma adı, yetenekler `phoneCall`, `runFullTrust`, `phoneLineTransportManagement` (kısıtlı), `bluetooth`.
- `Register-Package.ps1`: derler ve çıktı klasörünü gevşek (loose) paket olarak kaydeder (Geliştirici Modu gerekir). Sonra `FenpoS3` komutu exe'yi paket kimliğiyle çalıştırır.
- Akış referans uygulamalarla (MYSTRAVIL/klangbruecke, shrimqy/Sefirah) aynı: `GetDeviceSelector` → `FromId` → `RequestAccessAsync` → `RegisterApp` → `IsRegistered` → `ConnectAsync` → `PhoneCallManager.RequestStoreAsync` → `RequestLineWatcher` → `PhoneLine.DialWithResultAsync` / `PhoneCall.AcceptIncomingAsync`.
- Ek teşhis: LAF durumu (geçersiz bir token'la, hiçbir şeyi açmadan), `RegisterAppForUser`.

```powershell
.\spikes\S3-hfp\Register-Package.ps1
FenpoS3
```

Kaldırmak için: `Get-AppxPackage Fenpo.Spikes.S3Hfp | Remove-AppxPackage`.

## Sonuç (16 Ekim 2026, Windows 11 26H2 build 26300 Insider, iPhone 13)

| Adım | Sonuç |
|---|---|
| Paket kimliği (gevşek kayıt, imzasız, Geliştirici Modu) | Var |
| `PhoneLineTransportDevice` seçicisi | iPhone'un HFP Audio Gateway servisi bulundu |
| `AudioRoutingStatus` | İlk çalıştırmada `CanRouteToLocalDevice`, sonrakilerde `CannotRouteToLocalDevice` |
| LAF `phonelinetransportdevice_v1` | **`Unavailable`**: kilit var, açılmamış |
| `RequestAccessAsync` | `Allowed` |
| `RegisterApp` | Hata yok, ama **`IsRegistered` false kalıyor** |
| `RegisterAppForUser` | `UnauthorizedAccessException` (beklenen; çok kullanıcılı uygulama yeteneği yok) |
| `ConnectAsync` | `False` |
| `PhoneLine` | iPhone için hat **oluşmadı**. Görünen tek hat WhatsApp masaüstünün VoIP hattı (`canDial=False`) |

Elenenler:

- **Rolü başka bir uygulamanın tutması.** Phone Link'te iPhone yok, `PhoneExperienceHost` kapalıydı. iPhone iki taraftan kaldırılıp yeniden eşleştirildi ve ilk iş `FenpoS3` çalıştırıldı (belgeye göre kayıt, cihaz kaldırılınca silinir). Sonuç aynı.
- **`RequestAccessAsync` eksikliği** (klangbruecke'nin asıl hatası). Çağrılıyor, `Allowed` dönüyor.

Kalan açıklama: bu Windows sürümünde `PhoneLineTransportDevice` kaydı LAF kilidine bağlı. Sefirah, 22000 ve üzerinde LAF'ı atlıyor ve Windows 11'de çalıştığını söylüyor; klangbruecke Windows 10'da LAF `Unavailable` iken bile kaydı alabildi. Burada ise kayıt sessizce reddediliyor. Bu makine Insider sürümünde olduğu için aynı davranışın kararlı Windows 11'de (24H2, 25H2) olup olmadığı **bilinmiyor**. Kesin cevap için ya kararlı bir Windows 11'de deneme ya da Microsoft'tan LAF token'ı gerekiyor.

Denenmeyen: Sefirah'ın Windows'tan çıkarılmış bir anahtarla LAF token'ı üretmesi. Microsoft'un erişim kontrolünü dolanmak olduğu için Fenpo'da kullanılmıyor (DECISIONS #6).

## Karar

- **Aşama 2'de aramalar yedek yolla yapılır:** gelen arama ANCS'nin `IncomingCall` bildiriminden Ada'da gösterilir. "Yanıtla" ANCS olumlu aksiyonu, "Reddet" olumsuz aksiyonu gönderir. Ses iPhone'da kalır; arama başlatma PC'den yapılamaz. ANCS aksiyonları S1'de çalıştı, `IncomingCall` kategorisi henüz cihazda görülmedi.
- HFP yolu kapanmadı, beklemeye alındı: (1) kararlı bir Windows 11'de aynı paketle deneme, (2) Microsoft'a LAF erişim talebi. İkisinden biri olumlu dönerse HFP Aşama 2'ye geri girer.
- Ürün mimarisi buna hazır kurulur: arama servisi `ICallSource` arayüzünün arkasında. İlk uygulaması ANCS, ileride HFP.

## Ürün koduna etkisi

1. `Fenpo.Core`'da arama için kaynaktan bağımsız bir arayüz: gelen arama olayı, yanıtla, reddet; "PC'den ses" ve "numara çevir" yetenek bayrağıyla.
2. Ada'daki gelen arama görünümünde "Bilgisayardan yanıtla" yerine "Yanıtla" (iPhone'da açılır) ve "Reddet" gösterilir. Tasarımdaki üç düğmeli ada (`iPhone'da yanıtla`, `Reddet`, `Yanıtla`) iki düğmeye iner; bu tasarım değişikliği Buğra'nın onayına gider.
3. Aramalar sayfasındaki tuş takımı ve "Ses bilgisayarın hoparlör ve mikrofonundan geçer" metni HFP gelene kadar gizlenir. Arama geçmişi PBAP'tan gelmeye devam eder.
4. MSIX paketi `phoneLineTransportManagement` yeteneğini şimdilik taşımaz; HFP dönerse eklenir. Böylece Store yolu da açık kalır (PLAN açık konular, dağıtım).
5. Paketli konsol + takma ad + gevşek kayıt düzeni hızlı deneme için işe yarıyor; S7 (sanal kamera) MSIX kaydı denemesinde de kullanılabilir.

## Açık kalanlar

- Kararlı Windows 11'de (24H2/25H2) aynı paketle `RegisterApp`.
- Microsoft'a LAF talebi (`com.microsoft.windows.applicationmodel.phonelinetransportdevice_v1`).
- ANCS `IncomingCall` bildirimi ve olumlu/olumsuz aksiyonların gerçek bir aramada denenmesi.
