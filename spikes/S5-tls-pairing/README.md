# S5 (Windows tarafı): Wi‑Fi üzerinden TLS eşleşme

## Hedef

PC tarafında Fenpo veri kanalını (④) kurmak: ECDSA P-256 kendinden imzalı sertifika (DPAPI ile saklı), TLS 1.3 ve karşılıklı sertifika, QR'daki tek kullanımlık token ile `PairingRequest`/`PairingResult`, ardından `Hello` ve `Ping`, DNS-SD `_fenpo._tcp` kaydı. Telefon yerine aynı makinede çalışan bir simülatörle olumlu ve olumsuz durumları uçtan uca denemek. Protokol taslağındaki boşlukları (DECISIONS #23, #30) kapatacak değişikliği önermek.

## Yapılanlar

| Proje | Görevi |
|---|---|
| `Shared/` | `protocol/fenpo/v1/*.proto`'dan Grpc.Tools ile üretilen C# tipleri (sadece mesajlar); `[uint32 BE uzunluk][Envelope]` çerçeveleme (1 MB sınırı); kimlik (ECDSA P-256 sertifika, `%LOCALAPPDATA%\Fenpo\Spikes\S5` altında DPAPI ile); `fenpo://pair` URI'si |
| `PcServer/` | TLS 1.3 sunucusu (`SslStream`, istemci sertifikası zorunlu); 16 baytlık tek kullanımlık token, 5 dakika ömür; `PairingRequest` → telefonu **TLS'teki sertifikasıyla** sabitler, `PairingResult` (32 baytlık BLE anahtarı), token'ı yeniler; aynı bağlantıda `Hello` ve `Ping`/`Pong`; bilinmeyen sertifikaya `Error NOT_PAIRED`; QR (QRCoder: PNG ve konsolda); DNS-SD kaydı `DnsServiceRegister` (dnsapi.dll) ile |
| `PhoneSim/` | Telefonu oynar. `scenarios` aşağıdaki tabloyu çalıştırır; `browse` DNS-SD kaydını WinRT `DeviceInformation` ile arar |

Varsayılan olarak sunucu sadece loopback'i dinler (yerel test, güvenlik duvarı sorusu yok); `--lan` tüm arayüzleri dinler ve LAN adreslerini QR'a koyar.

```bash
dotnet run --project spikes/S5-tls-pairing/PcServer
dotnet run --project spikes/S5-tls-pairing/PhoneSim -- scenarios
```

`PhoneSim scenarios` sırası:

| # | Senaryo | Beklenen |
|---|---|---|
| 1 | DNS-SD kaydı görünüyor | `_fenpo._tcp` örneği, port ve TXT `id`, `v` |
| 2 | QR'daki parmak izi yanlış | Telefon el sıkışmayı keser |
| 3 | Sadece TLS 1.2 konuşan istemci | Reddedilir |
| 4 | İstemci sertifikası yok | Reddedilir |
| 5 | Yanlış token | `PairingResult` `TOKEN_INVALID` |
| 6 | Doğru QR | Eşleşir, `pc_id` QR'la aynı, BLE anahtarı 32 bayt; aynı bağlantıda `Hello` (PC, WINDOWS) ve `Ping`/`Pong` |
| 7 | Kullanılmış token tekrar | `TOKEN_INVALID` |
| 8 | Eşleşmiş sertifikayla yeniden bağlanma | `Hello` kabul |
| 9 | Bilinmeyen sertifikayla yeniden bağlanma | `Error NOT_PAIRED` |

## Sonuç (16 Ekim 2026, gece)

- Üç proje uyarısız derleniyor; protobuf tipleri `protocol/`'den sorunsuz üretiliyor.
- **Çalıştırılamadı:** Windows Defender, `PcServer.dll`'i hem `obj/` hem `bin/` altında **`Backdoor:Win32/Farfli!pz`** olarak işaretleyip karantinaya aldı (00:57 ve 00:58). `PhoneSim.dll` işaretlenmedi. `!pz` buluşsal bir imza; muhtemel tetikleyici, aynı ikili dosyada TCP dinleyici, DPAPI ve mDNS kaydının bir arada olması.
- Defender'ı kapatmak, dışlama eklemek ya da kodu algılamayı atlatacak şekilde değiştirmek yapılmadı. Bu karar Buğra'nın. Seçenekler:
  1. Geliştirme klasörü için Defender dışlaması (`C:\Users\bugra\source\repos\fenpo`). Geliştirmede yaygın, ama makine geneli bir güvenlik ayarı.
  2. Dosyayı Microsoft'a yanlış alarm olarak göndermek: <https://www.microsoft.com/wdsi/filesubmission>. Yanıt günler sürebilir.
- Henüz ölçülmeyenler: TLS 1.3 el sıkışma süresi ve şifre takımı, Schannel'ın bu sertifikayla sunucu olarak çalışıp çalışmadığı, DNS-SD kaydının aynı makineden görünüp görünmediği, QR boyutu. Tahmini URI uzunluğu ~190 karakter (QR sürüm 9–10, ECC M).

## Ürün koduna etkisi

1. **Defender yanlış alarmı ürün için de risk.** `Fenpo.exe` bu kodun hepsini taşıyacak. Yayından önce: imzalı ikili dosyalar ve MSIX, yayınlanacak her sürümü Microsoft'a önceden göndermek, CI'da Defender taraması. PLAN'ın dağıtım maddesine eklenmeli.
2. Kimlik DPAPI ile saklanan PFX; Schannel sunucusu için anahtar `EphemeralKeySet` olmadan yükleniyor (gerçek çalıştırmada doğrulanacak).
3. Telefon, `PairingRequest` içinde ne iddia ederse etsin, TLS'te sunduğu sertifikayla sabitleniyor.
4. DNS-SD, WinRT `DnssdServiceInstance` yerine `DnsServiceRegister` ile; TLS sunucusu sade bir `TcpListener` üzerinde kalabiliyor.

## Önerilen protokol değişikliği (DECISIONS #23, #30)

`beede75` (iOS oturumunun UUID commit'i) `main`'e alındıktan sonra, Buğra'nın onayıyla tek bir `protocol: ...` commit'i ve aynı commit'te `docs/ARCHITECTURE.md` güncellemesi:

1. **`PairingRequest.certificate_sha256` (5) kaldırılır, `reserved` olur.** PC telefonun parmak izini TLS'ten hesaplar; iddia edilen değer hiçbir şey kanıtlamaz.
2. **Eşleşme bağlantısı devam eder.** `PairingResult{accepted=true}` sonrası aynı bağlantı normal oturuma döner: telefon `Hello` gönderir, PC `Hello` ile cevap verir. Ret durumunda PC bağlantıyı kapatır. (Spike böyle yapıyor.)
3. **`Hello`'ya iki alan:** `string os_version = 7;` ("iOS 27.0.1"), `string model = 8;` ("iPhone14,5"). Cihaz satırındaki iOS sürümü buradan gelir (DECISIONS #30).
4. **6 haneli yedek kod.** Bugünkü tarif ("kod aynı token'a karşılık gelir") güvensiz: QR olmadan telefonun elinde parmak izi yok, 20 bitlik kod araya giren saldırgana (MITM) karşı koruma sağlamaz. Seçenekler:
   - (a) **Yedek kodu kaldırmak.** Kamera açılmıyorsa QR tekrar gösterilir; en basit yol.
   - (b) **Sayı karşılaştırma (SAS) + taahhüt.** Telefon PC'yi DNS-SD ile bulur, parmak izi sabitlemeden TLS kurar. Telefon önce `H(N_telefon)` taahhüdünü yollar, PC `N_pc`'yi yollar, telefon `N_telefon`'u açar. İki taraf da TLS 1.3 exporter değeri, `N_telefon` ve `N_pc` üzerinden 6 haneli bir sayı hesaplar ve iki ekranda gösterir; kullanıcı iki ekranda da onaylar. Taahhüt olmazsa saldırgan kendi anahtarını deneme yanılmayla seçip sayıları eşleyebilir. Bunun için 2–3 yeni mesaj gerekir (`PairingCommit`, `PairingNonce`, `PairingConfirm`). iOS'ta exporter `sec_protocol_metadata_create_secret` ile alınabilir.
   - Önerim (a), (b) ileride. Karar Buğra'nın.
5. **BLE kanalında AES-GCM.** Anahtar `PairingResult.ble_channel_key`. Önce tüm Envelope şifrelenir, sonra parçalanır. Nonce 12 bayt: `[1 bayt gönderen: 0x01 telefon, 0x02 PC][3 bayt sıfır][8 bayt sayaç, big-endian]`. AAD `"fenpo-ble-v1"`. Her gönderen sayacını kalıcı ve artan tutar; alıcı son gördüğünden küçük veya eşit sayacı reddeder (tekrar saldırısı).
6. **`TransferStreamHeader` çerçevesi.** Veri bağlantısı da `[uint32 BE uzunluk][TransferStreamHeader]` ile başlar, ardından dosyaların ham baytları teklif sırasıyla, bayt sayısı `FileInfo.size` toplamı kadar gelir.
