# S2: MAP ile mesaj okuma ve gönderme

## Hedef

iPhone'un Message Access Server'ına (MAS) .NET'ten kendi OBEX istemcimizle bağlanmak: klasörleri ve mesajları okumak, okundu işaretlemek, yeni mesaj bildirimlerini (MNS) almak ve SMS göndermek. RESEARCH §3'teki iPhone kısıtlarını cihazda görmek.

## Yapılanlar

`MapSpike/`: paketlenmemiş .NET 10 konsol uygulaması. Dış bağımlılık yok, OBEX elle yazıldı.

| Dosya | Görevi |
|---|---|
| `Obex.cs` | OBEX paketleri ve başlıkları, MAP uygulama parametreleri (TLV) |
| `ObexClient.cs` | Connect, SetPath, çok paketli GET ve PUT, Disconnect; her paket `captures/`'a yazılır |
| `MasClient.cs` | SDP okuma; klasör listesi, mesaj listesi, mesaj alma, okundu işaretleme, bildirim kaydı, gönderme |
| `MnsServer.cs` | `RfcommServiceProvider` ile 0x1133 MNS sunucusu ve SDP kaydı; olay raporlarını alır |
| `BMessage.cs` | Giden SMS için bMessage üretimi, gelen bMessage'dan özet |
| `Program.cs` | Açılış akışı ve komutlar |

```bash
dotnet run --project spikes/S2-map/MapSpike
```

Komutlar: `list <klasör> [adet]`, `get <handle>`, `read <handle>`, `unread <handle>`, `folders`, `notify on|off`, `send <numara> <metin>`, `q`.

## Sonuç (16 Ekim 2026, iPhone 13, cihazda)

### İzin

- İlk bağlantıda OBEX Connect **Forbidden** döndü. iPhone'da **Ayarlar > Bluetooth > (i) > Show Message Notifications** anahtarı açılınca bağlantı kuruldu. Anahtar bu ilk denemeden sonra göründü.

### SDP

| Öznitelik | Değer |
|---|---|
| Profil | MAP 1.4 (`0x1134`, `0x0104`) |
| RFCOMM kanalı | 8 |
| GOEP L2CAP PSM | `0x100B` (L2CAP üzerinden OBEX de mümkün; denenmedi) |
| Servis adı | "MAP MAS-iOS" |
| MAS örneği | 1 adet (id 0) |
| Desteklenen mesaj türleri | `0x02`: sadece `SMS_GSM` |
| MapSupportedFeatures | `0x0006027F` |

### MAS (mesaj erişimi)

- iPhone'un en büyük OBEX paketi 4000 bayt. Bağlanmak 40 ms sürüyor.
- **`SetPath telecom` → `msg` çalışıyor.** RESEARCH'teki "iPhone klasör gezinmeye izin vermiyor" bilgisi bu iOS sürümünde doğru değil.
- Klasörler: `inbox`, `sent`, `outbox`, `deleted`.
- **Gelen kutusu eşleşmeden önceki SMS'leri de veriyor:** eşleşme gece 23:50'de yapıldı, liste bir önceki sabah 09:00'a kadar gidiyordu. 10 mesajlık liste 3,4 KB, tek pakette ve 26 ms'de geldi.
- **Gönderilenler klasörü boş** geldi; telefondan önceden gönderilmiş mesajlar yok. Fenpo'dan gönderilen mesaj ise oraya düştü.
- Liste MAP 1.0 biçiminde. Bütün mesajlar `SMS_GSM`; iMessage ayrı bir tür olarak görünmüyor.
- `GetMessage` (bMessage):
  - Gönderenin rehberdeki adı vCard'daki `FN;CHARSET=UTF-8:` satırında geliyor; isim çözümü için PBAP şart değil.
  - Alıcı vCard'ı boş geliyor.
  - Standart dışı `NOTIFICATION:1` ve `LANGUAGE:UNKNOWN` satırları var.
- `SetMessageStatus` (okundu/okunmadı) çalışıyor; Buğra iPhone'da değişikliği gördü.

### MNS (yeni mesaj bildirimi)

- `SetNotificationRegistration` sonrası iPhone **260 ms içinde** bizim MNS sunucumuza bağlandı. Windows'ta paketlenmemiş bir süreçten `RfcommServiceProvider` ve SDP kaydı sorunsuz çalıştı.
- Gelen SMS'te `NewMessage` olayı geldi. Olay raporu MAP 1.0 biçiminde: gönderen ve metin yok, sadece handle, klasör ve tür var.
- Olaydan 2 ms sonra gönderilen `GetMessage` isteğine iPhone **33 saniye** sonra cevap verdi. Bunun sebebi görülmedi; telefonun kilitli olması veya mesajın henüz işlenmemiş olması olabilir.
- Buğra iki mesaj gönderdirdi ama sadece bir `NewMessage` olayı görüldü. İkincisinin (iMessage) olay üretmediği mi, yoksa hiç gönderilmediği mi belli değil.

### Gönderme (PushMessage)

- `outbox`'a bMessage PUT edildi. iPhone 1,8 saniyede `Success` ve yeni bir handle döndü.
- Ardından `MessageShift` (outbox → sent) ve `SendingSuccess` olayları geldi. **Mesaj karşıya ulaştı** (Buğra doğruladı). SMS mi iMessage mı olarak gittiği bilinmiyor.

## Karar

- **MAP .NET'ten, ek kütüphane olmadan yazılabiliyor.** OBEX katmanı küçük (birkaç yüz satır). MyPhone'a bağımlılık gerekmiyor, sadece referans olarak kalır.
- **Okuma, okundu işaretleme, bildirim ve gönderme çalışıyor.** Aşama 2'nin mesaj özellikleri planlandığı gibi yapılabilir.
- Yeni mesajın ilk gösterimi **ANCS'den** yapılır: Mesajlar bildirimi zaten başlık ve metinle anında geliyor. MAP sohbet geçmişi, yanıt ve okundu işaretleme için kullanılır. MNS sonrası `GetMessage` gecikebildiği için bildirim yolu ona bağlanmaz.
- **Sohbet görünümünde giden mesajlar eksik kalır:** iPhone'dan gönderilmiş eski mesajlar MAP'te yok. Fenpo'dan gönderilenler `sent` klasöründe tutuluyor. Arayüz bunu açıkça söylemeli (tasarımdaki sohbet ekranı iki taraflı geçmiş gösteriyor).

## Ürün koduna etkisi

1. `Fenpo.Bluetooth` içinde OBEX istemci ve sunucusu: paket ayrıştırma, çok paketli GET/PUT, bağlantı kimliği. MAS istemcisi ve MNS sunucusu ayrı sınıflar. Ayrıştırıcılar `fixtures/` ile test edilir.
2. Bağlantı `Forbidden` dönerse kullanıcıya "iPhone'da Ayarlar > Bluetooth > bu bilgisayar > Mesaj bildirimlerini göster'i aç" yönlendirmesi gösterilir. Eşleşme sihirbazına da bu adım eklenir.
3. Connect isteğinde `MapSupportedFeatures` gönderilir; iPhone'un MAP 1.1+ olay raporlarını (gönderen, metin) verip vermediği Aşama 2'de denenir. Vermiyorsa yeni mesaj bilgisi ANCS'den alınır.
4. bMessage ayrıştırıcısı `FN;CHARSET=UTF-8:` gibi parametreli satırları ve standart dışı satırları kabul eder.
5. `GetMessage` için zaman aşımı ve yeniden deneme olmalı; arayüzü kilitlememeli.
6. Mesaj handle'ları 15 haneli onaltılık dizeler. Bağlantılar arasında kalıcı olup olmadıkları (MAP 1.3 "persistent message handles", özellik biti 10) iPhone'da kapalı görünüyor; önbellek handle'a güvenmeden datetime ve gönderenle eşleştirmeli.

## Fixture'lar

Hepsi tek bir oturumun (`captures/20261016-001832.jsonl`) anonimleştirilmiş hali. İsimler, numaralar, mesaj metinleri ve konular değiştirildi. `LENGTH` ve OBEX uzunluk alanları yeniden hesaplandı. Handle'lar, tarihler, başlık sırası ve iPhone'a özgü satırlar gerçek. Ham kayıt `captures/` altında ve git'e girmiyor.

| Dosya | İçerik |
|---|---|
| `obex-mas-mns-session.jsonl` | Bütün OBEX paketleri, iki yönde: Connect, SetPath, klasör/mesaj listeleri, GetMessage, SetMessageStatus, bildirim kaydı, MNS Connect, olay raporları, PushMessage |
| `msg-listing-inbox.xml` | 10 mesajlık `x-bt/MAP-msg-listing` |
| `bmessage-incoming-shortcode.txt` | Kurum adından (alfanümerik gönderen) gelen SMS |
| `bmessage-incoming-contact.txt` | Rehberdeki bir kişiden gelen SMS, emoji içeriyor |
| `bmessage-outgoing.txt` | Fenpo'nun gönderdiği bMessage |
| `event-report-*.xml` | `NewMessage`, `MessageShift`, `SendingSuccess` |

## Açık kalanlar

- iMessage'ın `NewMessage` olayı üretip üretmediği ve gönderilen mesajın SMS mi iMessage mı olduğu.
- `GetMessage`'ın yeni bir mesajı otomatik okundu yapıp yapmadığı.
- MNS sonrası `GetMessage`'ın 33 saniyelik gecikmesinin sebebi.
- `MapSupportedFeatures` ile MAP 1.1+ olay raporları.
- Grup mesajı ve resimli mesaj (RESEARCH §3'e göre yok; bu oturumda denenmedi).
- L2CAP üzerinden OBEX (PSM `0x100B`).
