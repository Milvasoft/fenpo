# Pano: iOS neye izin veriyor, neye vermiyor

Buğra için sade bir özet. Kısaca: **PC'den iPhone'a** pano kendiliğinden gelebilir; ama iOS'un bunu arka planda kabul edip etmediği ancak cihazda belli olur. **iPhone'dan PC'ye** pano kendiliğinden gidemez; iOS buna izin vermez. Fenpo bunun yerine en kısa yolları sunuyor. Cihaz testleri: [TESTING.md › Pano, Fenpo'yu açmadan](TESTING.md#pano-fenpoyu-açmadan).

## PC → iPhone

**Ne oluyor:** PC'de bir şey kopyaladığında Windows'taki Fenpo bunu hemen telefona yollar. Metin Bluetooth'tan gider. Resim Bluetooth'a sığmaz; PC önce telefona "Wi‑Fi'yi aç" der (`WifiWanted`), resim de Wi‑Fi'den gelir.

**iOS'un izin verdiği:**
- Fenpo arka plandayken ya da telefon kilitliyken Bluetooth bağlantısı açık kalır. PC bir şey yollayınca iOS Fenpo'yu birkaç saniyeliğine uyandırır. Fenpo'yu uygulama değiştiriciden kaydırıp kapatsan bile iOS 26 ve AccessorySetupKit ile geri getirebiliyor (S4 testi).
- Uyanan Fenpo panoya yazmayı dener. Yazıp yazamadığı belgelenmemiş, bunu ilk kez bu build'de cihazda ölçüyoruz.

**iOS'un izin vermediği ya da sınırladığı:**
- Uyanma süresi kısadır: metin için yeter. Resimde Wi‑Fi bağlantısının 10 saniye içinde açılması gerekir; açılmazsa PC vazgeçer.
- 512 KB'tan büyük resimleri PC pano olarak hiç göndermiyor (Windows tarafının sınırı).
- Telefon yeniden başlatıldıysa Bluetooth, kilit bir kez açılana kadar gelmez.

**Bu build'de yapılanlar (derleniyor, cihazda denenmedi):**
- Panoya yazma artık türleri açıkça belirtiyor, böylece Fenpo hemen askıya alınsa da veri panoda kalıyor. Yazarken kısa bir arka plan süresi isteniyor.
- Her yazmadan sonra panonun gerçekten değişip değişmediğine bakılıyor. Değişmediyse, sen Fenpo'yu açtığında aynı öğe yeniden yazılıyor. Bu arada iPhone'da başka bir şey kopyaladıysan o ezilmiyor.
- Parola veya kod olarak işaretli öğe sadece bu iPhone'da kalıyor, Mac'e geçmiyor ve 2 dakika sonra panodan siliniyor.
- iOS Fenpo'yu Bluetooth için yeniden başlattığında kanalın kendiliğinden ayağa kalkmaması düzeltildi. Önceden telefon PC'ye kendini tanıtmayabiliyordu (Hello), PC de telefonu "bağlı değil" sayıp panoyu hiç göndermiyordu.
- **Pano paylaşımı** anahtarı artık gerçekten çalışıyor: kapalıyken PC'den gelen yazılmıyor, telefondan da gönderilmiyor.
- Kayıtlar dört olası arızayı birbirinden ayırıyor: Bluetooth kanalı ayakta değildi, iOS mesajı iletmedi, iOS yazmayı kabul etmedi, resim Wi‑Fi'den zamanında gelmedi. Kayıt pano içeriğini hiç yazmıyor, sadece türünü ve boyunu.

**Senin tarafında:** Fenpo'yu iPhone'da açman gerekmemeli. "İki tarafta da açmam gerekiyor" durumunun iPhone tarafı yukarıdakilerle ölçülecek. Windows tarafında Fenpo'nun tepside çalışıyor olması yetmeli; orası Windows oturumunun işi.

## iPhone → PC

**iOS'un izin vermediği:** Arka plandaki hiçbir uygulama panoyu okuyamaz. Apple bunu bilerek böyle yapıyor; yolu yok. Yani iPhone'da kopyaladığın şey Fenpo açılmadan kendiliğinden PC'ye gidemez. Ön plandaki uygulama da her okumada "Yapıştırmaya izin ver" diye sorar. Bunu kapatmak için: Ayarlar › Uygulamalar › Fenpo › Diğer Uygulamalardan Yapıştır › İzin Ver. iOS bu ayarın ne olduğunu uygulamaya söylemez.

**En kısa yollar:**

| Yol | Kaç dokunuş | Fenpo açılıyor mu | Ne gider |
|---|---|---|---|
| **Arkaya iki dokun** (bir kez kestirme kurulur) | 1 | Hayır | Metin, bağlantı |
| **Eylem düğmesi** (iPhone 15 Pro ve sonrası) | 1 | Bir an | Metin, bağlantı, resim |
| **Denetim Merkezi / kilit ekranı** denetimi | 2 | Bir an | Metin, bağlantı, resim |
| **Siri:** "Fenpo ile panoyu PC'ye gönder" | Ses | Bir an | Metin, bağlantı, resim |
| **Paylaş › Fenpo › PC panosuna kopyala** | 3 | Hayır | Metin, bağlantı, tek resim |
| **Fenpo'yu açmak** | Açınca 1 (ya da 0) | Evet | Metin, bağlantı, resim |

- **Arkaya iki dokun:** iOS'ta Kestirmeler uygulaması panoyu okuyabiliyor. "Panoyu Al" + Fenpo'nun "Metni PC'ye gönder" eyleminden oluşan bir kestirme Fenpo'yu açmadan çalışır. Fenpo Ayarlar › Arkaya iki dokun sayfası adım adım anlatıyor ve Kestirmeler'i açan bir düğme var. Kestirmeyi tek dokunuşla kuramıyoruz. Bunun için imzalı bir kestirme dosyası lazım ve bunu bir Mac'te iCloud hesabıyla bir kez üretmek gerekiyor. Arkaya Dokun ayarına doğrudan götüren genel bir bağlantı da yok. Gizli `App-prefs:` bağlantıları App Store'da reddedilme sebebi, onları kullanmadım. İstersen ortağın Mac'inde kestirmeyi bir kez kurup iCloud bağlantısını paylaşırız; o zaman kurulum tek dokunuşa iner.
- **Fenpo'yu açınca (yeni):** Ayarlar › **Açılınca panoyu gönder**.
  - **Sor** (varsayılan): Fenpo'yu açtığında panoda yeni bir şey varsa Ana Sayfa'da bir kart ve sistemin **Yapıştır** düğmesi çıkar. Bu düğme için iOS izin sormaz. Fenpo içeriği sen dokunmadan okumaz.
  - **Otomatik**: Açar açmaz gönderir. "Diğer Uygulamalardan Yapıştır" İzin Ver değilse iOS her seferinde sorar. Reddedersen aynı pano için bir daha sorulmaz.
  - **Kapalı**.
  - Fenpo panoya bakmadan önce sadece "değişti mi, metin mi resim mi, parola gibi işaretli mi, sayı mı" bilgisini okur. Bunlar izin istemez.
- **Paylaş menüsü:** Önceden sadece bağlantı ("PC'de aç") ve dosya gönderiyordu. Paylaşılan metin dosya olarak gidiyordu. Artık metin, bağlantı ve tek resim için **PC panosuna kopyala** düğmesi var.

**Parolalar ve kodlar:** Parola yöneticilerinin işaretlediği içerik ve 4–8 haneli kodlar (ör. SMS kodu) "hassas" sayılıyor. Otomatik modda bile kendiliğinden gitmiyor, senin dokunuşunu bekliyor. Gidince PC'nin pano listesinde görünmüyor. Ters yönde de aynısı geçerli: PC'den gelen hassas öğe 2 dakika sonra iPhone panosundan siliniyor.

## Durum

- Derleniyor: CI'da yeşil olunca. Cihazda **denenmedi**. TestFlight build'i çıkınca TESTING.md'deki C1–C17 adımları.
- En önemli soru C2: Fenpo arka plandayken PC'nin kopyası iPhone panosuna yazılabiliyor mu? Cevap "hayır" çıkarsa iOS'un sınırı demektir. O zaman PC'den gelen öğe Fenpo'yu açınca yazılır; bunu bu build zaten yapıyor. Ayrıca bildirimle haber verme gibi seçenekleri konuşuruz.
