# iOS cihaz testleri

| Kilometre taşı | Kim, nerede | Bölüm |
|---|---|---|
| 1: S4 ve S6 | Ortak, kendi Mac'i ve iPhone'u; PC yerine Mac'teki test aracı | Aşağıda, 1–5 |
| 2: gerçek PC ile eşleşme | Buğra, Windows PC'si ve TestFlight'tan kurduğu iPhone | [Kilometre taşı 2](#kilometre-taşı-2-gerçek-pc-ile-eşleşme) |
| TestFlight'a yükleme | Ortak, Mac'te | [TestFlight](#testflight) |

# Kilometre taşı 1 (S4, S6)

Bu liste Mac'i ve iPhone'u olan ortak için. Windows uygulamasına gerek yok: PC'nin yerini Mac'te çalışan küçük bir araç (`FenpoTestPeripheral`) alıyor. Araç, Windows'un yapacağı gibi aynı Bluetooth servisini yayınlıyor, aynı şifrelemeyi ve parçalamayı kullanıyor.

İki soruya cevap arıyoruz (DECISIONS #14):

- **S4:** Fenpo arka plandayken, kilitliyken, zorla kapatıldıktan sonra ve Bluetooth kapatılıp açıldıktan sonra Bluetooth üzerinden uyanıyor ve mesaj alıyor mu?
- **S6:** PC'den gelen pano içeriği, uygulama arka plandayken iPhone'un panosuna yazılabiliyor mu?

Sonuçlar mimariyi değiştirebilir (RESEARCH §5, §6). Gördüğünüzü olduğu gibi yazın; "çalışmadı" da değerli bir sonuç.

## Gerekenler

| | |
|---|---|
| Mac | Apple silicon, macOS Tahoe 26.6 veya sonrası, Bluetooth açık |
| Xcode | 26.6 veya 27 (CI 26.6 ile derliyor) |
| iPhone | iOS 26 veya 27. Elinizde iOS 18 bir iPhone varsa T5–T7'yi onda da tekrarlayın: zorla kapatma sonrası geri gelme iOS 26 ile değişti |
| Hesap | Milvasoft Apple Developer ekibinde bir Apple hesabı (Team ID `2AAA8VGR55`) |
| Araçlar | `brew install xcodegen protobuf` |

iPhone'da **Ayarlar → Gizlilik ve Güvenlik → Geliştirici Modu** açık olmalı (Xcode'dan kurulan uygulamalar için).

## 1. Derleme ve kurulum

Buğra size test edilecek commit'i söyler. O commit'in CI'ı yeşil olmalı: <https://github.com/Milvasoft/fenpo/actions/workflows/ios.yml>.

```bash
git clone https://github.com/Milvasoft/fenpo.git
cd fenpo
git checkout <commit>
ios/scripts/generate-proto.sh
cd ios && xcodegen generate && open Fenpo.xcodeproj
```

1. Xcode'da üstteki şemadan **Fenpo**'yu ve kablolu iPhone'u seçin.
2. **Signing & Capabilities**'te ekip Milvasoft olmalı ("Automatically manage signing" açık). Uygulama kimlikleri (`com.fenpo.app`, `.share`, `.widgets`) ve App Group (`group.com.fenpo`) hesapta hazır.
3. **Product → Run** ile kurun. Uygulama açılınca Xcode'da **Stop**'a basın.
4. **Testlere başlamadan önce uygulamayı ana ekrandan kendiniz açın.** Xcode'un hata ayıklayıcısı bağlıyken iOS uygulamayı askıya almaz; arka plan testleri yanlış sonuç verir.

## 2. Mac'te test aracını çalıştırma

Ayrı bir Terminal penceresinde, reponun kökünde:

```bash
swift run --package-path ios/Tools/FenpoTestPeripheral 2>&1 | tee ~/fenpo-peripheral.log
```

- macOS Bluetooth izni sorarsa verin (Sistem Ayarları → Gizlilik ve Güvenlik → Bluetooth → Terminal).
- `Advertising Fenpo Test with the Fenpo service` satırını görmelisiniz.
- `help` komutları listeler. Pencereyi testler boyunca açık tutun; çıktı `~/fenpo-peripheral.log` dosyasına da yazılır.

## 3. Test eşleşmesi

1. iPhone'da Fenpo → **Ayarlar** → en alttaki **Fenpo 0.1.0 (…)** yazısına 7 kez dokunun. **Geliştirici** menüsü açılır; bundan sonra Ayarlar'da da görünür.
2. **Test Mac'iyle eşleş**'e dokunun. iOS'un aksesuar seçicisi açılır, **Fenpo Test**'i seçin.
3. iOS bir Bluetooth eşleşme isteği gösterir, onaylayın.
4. Beklenen sonuçlar:
   - Mac'te `Phone subscribed`, `← Hello`, `→ Hello`, `→ PcStatus` satırları görünür.
   - iPhone'da "Test Mac'iyle eşleşildi." yazar.
   - Geliştirici → Durum'da **Bluetooth** `ready`, **Bluetooth'taki PC** `Fenpo Test Mac` olur.

Mac ile iPhone aynı Apple hesabını kullanıyorsa eşleşme penceresi hiç çıkmayabilir. Çıkıp çıkmadığını not edin.

## 4. Testler

Her testte üç şeyi not edin: Mac'teki çıktı, iPhone'daki kayıt (Geliştirici → Kayıt) ve beklenenle aynı olup olmadığı. Kayıttaki `app background` ve `app active` ifadeleri, mesaj geldiğinde uygulamanın hangi durumda olduğunu gösterir. `Launch ..., reason: Bluetooth restoration` satırı, iOS'un uygulamayı Bluetooth yüzünden yeniden başlattığı anlamına gelir.

### S4: arka planda Bluetooth

| # | Adım | Beklenen |
|---|---|---|
| T1 | Fenpo açıkken Mac'te `ping` | Mac'te 1 saniye içinde `← Pong`; iPhone kaydında `Received Ping ..., app active` |
| T2 | Fenpo'yu ana ekrana alın (kapatmadan), 1 dakika bekleyin, `ping` | `← Pong`; kayıtta `app background` |
| T3 | iPhone'u kilitleyin, 5 dakika bekleyin, `ping` | `← Pong`; kayıtta `app background` |
| T4 | Kilitli iPhone'u 30 dakika bırakın, sonra `ping` | `← Pong`. Kayıtta araya bir `Launch ..., reason: Bluetooth restoration` girdiyse iOS uygulamayı bellekten atıp Bluetooth için yeniden başlatmış demektir. Not edin |
| T5 | Uygulama değiştiriciden Fenpo'yu yukarı kaydırarak **zorla kapatın**. 1 dakika bekleyin, `ping` (gerekirse birkaç kez) | iOS 26+ ve AccessorySetupKit ile: Mac'te `← Pong`, kayıtta `reason: Bluetooth restoration`. Pong gelmezse: Mac'te Ctrl-C ile aracı kapatıp yeniden açın, 1 dakika bekleyin, tekrar `ping` |
| T6 | Denetim Merkezi'nden Bluetooth'u kapatın, 10 saniye bekleyin, açın. Fenpo'yu açmadan `ping` | Mac'te yeniden `Phone subscribed`, sonra `← Pong` |
| T7 | Aynısını **Ayarlar → Bluetooth** anahtarıyla yapın | TN3115'e göre burada uygulama geri gelmeyebilir. Ne olduğunu yazın |
| T8 | Mac'te aracı Ctrl-C ile kapatın, 2 dakika sonra yeniden çalıştırın. Fenpo'yu açmayın | 1–2 dakika içinde Mac'te kendiliğinden `Phone subscribed` ve `← Hello` |
| T9 | iPhone'u yeniden başlatın, kilidi bir kez açın, Fenpo'yu açmayın. `ping` | `← Pong`; kayıtta `reason: Bluetooth restoration` |
| T10 | iPhone kilitliyken Mac'te `ping-every 60`, 30 dakika bekleyin, `stop` | Kaç Ping'e Pong geldiğini sayın (Mac çıktısında `← Pong` satırları) |

### S6: arka planda pano

Panoyu kontrol etmek için Notlar'da yeni bir not açıp yapıştırın.

| # | Adım | Beklenen |
|---|---|---|
| T11 | Fenpo açıkken `clip Merhaba ön plan` | Notlar'a "Merhaba ön plan" yapışır; kayıtta `Clipboard from PC: text ..., app active, changeCount n -> n+1` |
| T12 | `clip-in 20 Merhaba arka plan`, hemen Fenpo'dan çıkıp Notlar'ı açın, 25 saniye bekleyip yapıştırın | Asıl soru bu: yapışan metin "Merhaba arka plan" mı? Kayıttaki `app background` satırında `changeCount` arttı mı? |
| T13 | `clip-in 20 Merhaba kilit`, iPhone'u kilitleyin, 30 saniye sonra kilidi açın, Notlar'a yapıştırın | Aynı soru, kilitliyken |
| T14 | T5'te zorla kapatılan uygulama geri geldiyse: Fenpo'yu zorla kapatın, `clip Merhaba kapalı`, 1 dakika sonra yapıştırın | Yapışan metni ve kayıttaki `Launch` satırını yazın |

### Pano kısayolları (Aşama 3'ün erken denemesi, isteğe bağlı)

Önce Denetim Merkezi'ni düzenleyip **Kontrol Ekle**'den Fenpo'nun üç düğmesini ekleyin: Panoyu gönder, PC'den yapıştır, PC'yi kilitle. Her testten sonra iPhone kaydındaki `... runs in the app process` ya da `... runs in the widget extension process` satırını not edin. Düğmenin işi hangi süreçte yaptığı henüz bilinmiyor, bu testlerin asıl sorusu bu.

| # | Adım | Beklenen |
|---|---|---|
| T15 | Notlar'da bir kelime kopyalayın, Denetim Merkezi'nden **Panoyu gönder** | Fenpo kısa bir an açılır; Mac'te `clipboard from phone: "..."` |
| T16 | Mac'te `pc-clip Merhaba PC`, iPhone'da Denetim Merkezi'nden **PC'den yapıştır**, sonra Notlar'a yapıştırın | "Merhaba PC" yapışır. Fenpo açık değilken ve kilit ekranından da deneyin |
| T17 | Denetim Merkezi'nden **PC'yi kilitle** | Mac'te `the phone asked to lock the PC` |
| T18 | Kısayollar'da yeni bir kısayol yapın: **Panoyu Al** → Fenpo **Metni PC'ye gönder**. Ayarlar → Erişilebilirlik → Dokunma → Arkaya Dokun'a bağlayın. Başka bir uygulamada bir şey kopyalayıp telefonun arkasına iki kez dokunun | Fenpo açılmadan Mac'te `clipboard from phone` |

### Telefonu çaldır, kilit ekranı widget'ları, PC'de aç (isteğe bağlı)

Test eşleşmesinden sonra Fenpo öne geldiğinde bildirim izni sorulur; izin verin.

| # | Adım | Beklenen |
|---|---|---|
| T19 | Fenpo açıkken Mac'te `ring` | "Fenpo çalıyor" ekranı ve ses; sessiz anahtar açıkken bile çalar. **Durdur** sesi keser. Bir kez de Mac'ten `ring-stop` ile durdurun |
| T20 | Fenpo arka plandayken ve iPhone kilitliyken `ring` | Sesli bir bildirim gelir. Sessiz modda ses beklenmez (iOS kısıtı). Bildirime dokununca Fenpo açılır ve ses durur |
| T21 | Kilit ekranını düzenleyip Fenpo'nun **PC durumu**, **PC pili** ve **PC'den yapıştır** widget'larını ekleyin. Mac'te `battery 64` | PC pili halkası %64'ü gösterir, PC durumu "Fenpo Test · Bağlı" der. Mac'te aracı kapatınca birkaç saniye içinde "Bağlı değil" olur. Yapıştır widget'ı T16'daki gibi çalışır |
| T22 | Kısayollar'da yeni kısayol: **Paylaşım Sayfasında Göster** (girdi: URL) → Fenpo **PC'de aç**. Safari'de bir sayfayı bu kısayolla paylaşın | Mac'te `open on PC: https://...` |

Paylaşım menüsündeki Fenpo uzantısının **PC'de aç** düğmesi Wi‑Fi kanalını kullanır. Windows'ta Wi‑Fi eşleşmesi gelince kilometre taşı 2'de denenecek.

## 5. Geri bildirim

Buğra'ya şunları gönderin:

1. Commit (`git rev-parse --short HEAD`), Xcode sürümü, iPhone modeli ve iOS sürümü.
2. Aşağıdaki tabloyu doldurun:

   | Test | Sonuç (✓/✗/kısmen) | Not |
   |---|---|---|
   | T1 | | |
   | … | | |
   | T22 | | |

3. Uygulama kaydı: Geliştirici → Kayıt → **Dışa aktar** (Mac'e gönderin, `fenpo-log.txt`).
4. Mac aracının kaydı: `~/fenpo-peripheral.log`.
5. Eşleşme penceresi çıktı mı, aksesuar seçicide "Fenpo Test" nasıl göründü (ekran görüntüsü yeter).

## Sorun giderme

| Belirti | Ne yapmalı |
|---|---|
| Seçicide "Fenpo Test" yok | Mac'te `status` yazın: Bluetooth `5` olmalı ve `Advertising` satırı görünmüş olmalı. Terminal'in Bluetooth iznini kontrol edin |
| Mac'te `Dropped message: replayed` ya da iPhone kaydında `replayed` | Sayaçlar uyuşmuyor. iPhone'da Geliştirici → **Eşleşmeyi ve kimliği sil**, Mac'te `~/Library/Application Support/FenpoTestPeripheral/counters.json` dosyasını silin, 3. adımı tekrarlayın. Bu dosyayı iPhone'daki eşleşmeyi sıfırlamadan silmeyin |
| `Not sent: the phone is not subscribed` | Bağlantı yok. iPhone'da Geliştirici → Durum'daki Bluetooth değerine bakın; `connecting` ise birkaç saniye bekleyin |
| Xcode imzalama hatası | Xcode → Settings → Accounts'ta Milvasoft ekibi görünmeli; iPhone kabloyla bağlı ve güvenilir olmalı |

## Notlar

- Test aracının şifreleme anahtarı sabit ve herkese açık (`TestPeripheral.bleChannelKey`). Sadece Geliştirici menüsündeki test eşleşmesinde kullanılır; gerçek bir PC için hiçbir zaman kullanılmaz.
- Kayıtta pano içeriği yer almaz, sadece türü ve uzunluğu yazılır.
- Gerçek Windows uygulamasıyla uçtan uca eşleşme (QR → Wi‑Fi → Bluetooth) kilometre taşı 2'de denenecek.

# Kilometre taşı 2: gerçek PC ile eşleşme

Buğra'nın Windows PC'si ve iPhone'u ile, QR → Wi‑Fi → Bluetooth eşleşmesinin uçtan uca denenmesi (DECISIONS #14, #15). Windows tarafının eşleşmesi ve kontrol kanalı `main`'de (468655e), BLE kanalı da öyle (f65f0e9).

## Hazırlık

- **Windows:** `main`'deki son Fenpo derlemesi açık. Phone Link kapalı (Bluetooth bağlantısını tutuyor). PC ile iPhone aynı Wi‑Fi ağında. Windows güvenlik duvarı ilk açılışta Fenpo için izin sorarsa **Özel ağlar**'a izin verin.
- **iPhone:** Fenpo'yu TestFlight'tan kurun (aşağıdaki TestFlight bölümü). Daha önce Mac'le test eşleşmesi yapıldıysa önce Geliştirici → **Eşleşmeyi ve kimliği sil**.
- iPhone, Windows Ayarlar'dan zaten Bluetooth ile eşleşmişse (Aşama 1'in uygulamasız eşleşmesi, DECISIONS #28), M2'de iOS'un eşleşme penceresi çıkmayabilir. Bunu not edin.

## Testler

| # | Adım | Beklenen |
|---|---|---|
| M1 | Windows'ta **iPhone'unu bağla** ekranını açın. iPhone'da Fenpo → **Kodu okut** ile QR'ı okutun | Tarayıcı açılınca iOS yerel ağ izni sorar; **İzin Ver**. "Neredeyse bitti" ekranında **Wi‑Fi bağlantısı** işaretlenir: "<PC> ile şifreli kanal kuruldu" |
| M2 | **Bluetooth'u bağla** | Aksesuar seçicisi açılır. PC hangi adla görünüyor (QR'daki ad mı, Bluetooth adı mı)? Seçin; iOS eşleşme isteği gelir, onaylayın |
| M3 | iOS "Bildirimlerini paylaşmak istiyor" diye sorar; **İzin Ver** | **Bildirim paylaşımı** adımı işaretlenir (PC'den `PcStatus`); **Bitti** düğmesi çıkar |
| M4 | **Bitti** | Ana ekranda PC kartı: ad ve yeşil Bluetooth. Geliştirici → Durum: Bluetooth `ready`, Bluetooth'taki PC adı ve özellikler dolu |
| M5 | Windows'ta telefon satırına bakın | iOS sürümü ve model (Hello'daki `os_version`, `model`) doğru |
| M6 | Başka bir telefondan bu iPhone'a mesaj gönderin | Windows'ta bildirim görünür (ANCS, aynı Bluetooth bağı) |
| M7 | Windows'ta Fenpo'yu kapatıp açın; iPhone'da Fenpo'yu açmayın | 1 dakika içinde yeniden bağlanır: Windows'ta telefon bağlı görünür, iPhone kaydında `Received Hello` |
| M8 | PC'yi yeniden başlatın | M7 ile aynı |
| M8b | iPhone'da Fenpo arka plandayken Windows'ta yalnızca Fenpo'yu yeniden başlatın (ya da PC'nin Bluetooth'unu kapatıp açın) | Birkaç saniye içinde Windows kaydında "Fenpo BLE channel opened"; iPhone kaydında `Fenpo service changed on the PC; rediscovering` (ya da `Fenpo service missing; trying again`), `Subscribed to tx; sending Hello` |
| M9 | Tarayıcıyı tekrar açıp 10 saniye bekleyin, kapatın. Geliştirici → Kayıt | `[Bonjour] Found ...` satırında PC'nin `_fenpo._tcp` kaydı ve TXT'deki `id`, `v`. Windows tarafı bunun telefonda doğrulanmasını istedi |
| M10 | iPhone hücresel veride, Wi‑Fi kapalıyken QR okutun | "Tamamlanmadı" ve **Yeniden dene**. Mesaj, ne yapılacağını anlatıyor mu? |
| M11 | QR'ın fotoğrafını çekin, 6 dakika bekleyin (QR 5 dakikada yenilenir), fotoğraftaki eski QR'ı okutun | Eşleşme reddedilir (`TOKEN_INVALID`), "Tamamlanmadı" |
| M12 | PC sekmesi → **Eşleştirmeyi kaldır** | Ana ekran "Bilgisayarını ekle"ye döner; aksesuar iOS Ayarlar → Gizlilik ve Güvenlik → Aksesuarlar'dan da kalkar |

## Gerçek PC ile özellikler

Windows tarafı bunların hepsini sunuyor (pano, `ClipboardRequest`, `LockPc`, `RingDevice`, `OpenUrl`, `DeviceState` ve odak, dosya aktarımı, Kamera sayfasındaki önizleme, fare, klavye ve sunum kumandası). M1–M4 bittikten sonra, iPhone ile PC aynı Wi‑Fi'dayken:

| # | Adım | Beklenen |
|---|---|---|
| F1 | Fenpo'yu açın, Geliştirici → Durum | **Wi‑Fi kanalı** `connected`, **PC adresleri** dolu (PC'nin `DeviceState.addresses`'i) |
| F2 | Bir metin kopyalayın, ana ekranda **Panoyu gönder** | PC'de yapıştırınca aynı metin. Kısa metin Bluetooth'tan gider (kayıtta `over BLE`) |
| F3 | 5 KB'tan uzun bir metinle F2 | Kayıtta `Too large for BLE, using Wi-Fi`, PC'de metin tam |
| F4 | PC'de bir şey kopyalayın, iPhone'da **PC'den yapıştır** | iPhone panosunda aynı metin |
| F5 | Denetim Merkezi'ne Fenpo düğmelerini ekleyip **PC'yi kilitle** | Windows kilitlenir. Fenpo kapalıyken de dene; olmazsa kayıttaki süreç adını yazın |
| F6 | Safari'de bir sayfayı Paylaş → Fenpo → **PC'de aç** | Sayfa PC'nin tarayıcısında açılır |
| F7 | Windows'tan **Telefonu çaldır**, iPhone kilitliyken | Bildirim ve ses; Fenpo'yu açınca çalma ekranı, **Durdur** |
| F8 | Fenpo → Aktarımlar → **+**, 3 fotoğraf seçin | Aktarımlar'da "Sürüyor" satırı ilerler; PC'nin indirme klasöründe 3 dosya; satır "Biten"e geçer |
| F9 | Fotoğraflar'da 200 MB'tan büyük bir video → Paylaş → Fenpo → **1 dosyayı PC'ye gönder** | Paylaşım sayfasında ilerleme; PC'de video tam, boyutu aynı |
| F10 | F8'i büyük bir videoyla (10 MB üstü) tekrarlayın, gönderim sürerken iPhone'u kilitleyin | Kilit ekranında Canlı Etkinlik: "PC’ye gönderiliyor", yüzde ve çubuk; Dinamik Ada'lı telefonda adada yüzde. Aktarım bitince kaybolur |
| F11 | F10 sürerken Fenpo'dan başka uygulamaya geçin, 1 dakika bekleyin | Aktarım sürer ve biter (arka plan süresi). Bitmezse kayıttaki son satırları yazın |
| F12 | Windows'tan iPhone'a 2 dosya gönderin | iPhone'da "Bilgisayardan dosya geldi" bildirimi; Dosyalar → iPhone'umda → Fenpo içinde iki dosya; Aktarımlar'da satırlar, dokununca önizleme |
| F13 | Aynı adla bir dosyayı ikinci kez gönderin | İkincisi "ad (2).uzantı" olarak kaydedilir, ilki silinmez |
| F14 | Aktarım sürerken PC'de iptal edin | iPhone'da satır "İptal edildi" ya da "Tamamlanmadı"; yarım dosya Fenpo klasöründe kalmaz |
| F14b | Fenpo arka planda (kapatmadan başka uygulamaya geç), Windows'tan bir dosya ve bir resimli pano öğesi gönder | Kayıtta `Wi-Fi wanted by PC for …`, sonra `Wi-Fi channel open for the PC`; dosya Dosyalar › Fenpo'ya, resim panoya gelir. 30 sn sonra `Wi-Fi channel idle; closing it` |
| F15 | iPhone'da Fenpo açıkken Windows'ta Fenpo → **Kamera** sayfasında önizlemeyi başlatın (PC 1920×1080, 30 fps, 6 Mbps, H.264, sessiz ister) | iPhone'da kamera ekranı açılır, PC'de görüntü gelir. Gecikme yaklaşık kaç saniye? Diğer uygulamalar için web kamerası aygıtı PC'de henüz yok |
| F16 | Kamera açıkken 0,5 / 1× / 2, **Çevir**; Ortala ve Mikrofon düğmeleri yok (18 Ekim'de kaldırıldı) | PC'deki görüntü değişir; görüntü yan yatmıyor mu? |
| F17 | Fenpo arka plandayken F15 | iPhone'da "Bilgisayarın kamerayı kullanmak istiyor" bildirimi; dokununca kamera başlar |
| F18 | Kamera açıkken **Ekranı karart**, 5 dakika bekleyin | Görüntü sürer; telefon ne kadar ısındı, pil ne kadar düştü? |
| F19 | Kamerayı iPhone'da **Kamerayı kapat** ile, sonra PC'deki önizlemeyi kapatarak durdurun | İki yolda da kamera kapanır, PC'de görüntü donmuş kalmaz |
| F20 | Windows'ta **Odak modunu eşitle** açık. iOS Ayarlar → Odak → Rahatsız Etme → Filtre ekle → Fenpo, **PC’de sustur** açık. Rahatsız Etme'yi açıp kapatın | PC'de Fenpo bildirimleri odak açıkken susar (Bildirimler sayfasında yine listelenir, gelen aramalar Ada'da görünür), kapanınca döner |
| F21 | Fenpo Ayarlar → **Odak modunu eşitle** kapalıyken F20 | PC susmaz |
| F22 | Rahatsız Etme açıkken iPhone'da Fenpo'yu uygulama değiştiriciden kapatın, sonra yeniden açın | Bağlantı kopunca PC susmayı bırakır; Fenpo yeniden bağlanınca (Hello) odak durumu tekrar gönderilir ve PC yine susar. Kayıtta `Sent Focus state` |

| F23 | Kamera açıkken telefonu dik, yatay sola ve yatay sağa çevirin | PC'deki görüntü her durumda düz; dikte üstü ve altı kırpılmış 16:9 resim, boyut değişmez |
| F24 | Kamera açıkken iPhone'da sol üstteki **X** | Kamera iPhone'da hemen kapanır, PC "durdu" gösterir; kayıtta `Camera stopped: close button`. PC kamerayı kendiliğinden yeniden açıyorsa bunu yazın |
| R1 | PC sekmesi → **Fare ve klavye**. Tek parmakla kaydırın, dokunun | İmleç akıcı oynar, dokunuş sol tık |
| R2 | İki parmakla yukarı aşağı kaydırın; iki parmakla dokunun | Sayfa kayar (yön Windows dokunmatik yüzeyi gibi mi?); iki parmak dokunuşu sağ tık menüsünü açar |
| R3 | Bir pencere başlığında basılı tutun (titreşim), sonra parmağı oynatın, bırakın | Pencere sürüklenir, bırakınca bırakılır |
| R4 | **PC’ye yaz** alanına Türkçe karakterli bir cümle yazın, bir harf silin, Return | PC'de aynı metin (ş, ğ, ı doğru), silme ve Enter işler |
| R5 | Esc, Tab, oklar, Sil, Enter, **Başlat** tuşları | Her biri PC'de aynı tuşu basar; Başlat menüsü açılır |
| R6 | PowerPoint'te sunum açık: **Sunum kumandası** → **Başlat**, **Sonraki**, **Önceki**, **Siyah ekran**, **Bitir** | Slayt gösterisi başlar, ilerler, geri gider, ekran kararır, gösteri biter |
| R7 | Lazer alanında basılı tutup gezdirin, bırakın | PC ekranında nokta parmağı izler, bırakınca kaybolur |
| R8 | **PC’nin ekranını açık tut** açık, PC'nin ekran kapanma süresinden uzun bekleyin; sonra kumandadan çıkın | Açıkken ekran kapanmaz; çıkınca normale döner |
| R9 | Kısayollar'da **Sonraki slayt** eylemini Eylem Düğmesi'ne atayın, Fenpo kapalıyken basın | Sonraki slayta geçer; olmazsa kayıttaki `[Intents]` satırını yazın |
| R10 | Wi‑Fi kapalı, sadece Bluetooth ile R1 ve R6 | Çalışır, imleç biraz daha seyrek güncellenir |

| N1 | Windows'ta Fenpo'da bildirimlerini paylaşacağın bir uygulama seç (ör. Outlook), PC'de bir bildirim gelsin; iPhone kilitli ve Fenpo kapalıyken de dene | iPhone'da aynı başlık ve metinle bildirim; PC'de kapatınca iPhone'dan da kalkar. Wi‑Fi varken uygulama simgesi görünür |
| N2 | Fenpo Ayarlar → **PC bildirimleri** kapalıyken N1 | iPhone'da bildirim çıkmaz |
| N3 | iOS Ayarlar → Bluetooth → PC ⓘ → **Sistem Bildirimlerini Paylaş** kapalı | Fenpo ana ekranında "PC, iPhone’un bildirimlerini almıyor" kartı ve nasıl açılacağı; açınca kart kalkar |
| H1 | Fenpo Ayarlar → **Kişisel Erişim Noktası**: ad ve parolayı yaz, **Kaydet** | Windows'ta erişim noktası bilgisi görünür (PC `HotspotInfo` aldı) |
| H2 | Windows'ta **Erişim noktasına bağlan** | iPhone'da "Bilgisayarın erişim noktana katılmak istiyor" bildirimi; Kişisel Erişim Noktası'nı açınca PC bir dakika içinde katılır |
| H3 | **Kaldır**, sonra H2 | PC "ayarlanmadı" der |
| P1 | Windows'ta telefonun fotoğraflarına göz at (Wi‑Fi) | En yeniler önce, küçük resimler gelir; aşağı kaydırınca sonraki sayfa. İzin hiç sorulmadıysa Fenpo Ayarlar → İzinler → **Fotoğraflar** ile izin ver |
| P2 | PC'de birkaç fotoğraf ve bir video seç, indir | PC sormadan alır; HEIC ve MOV olduğu gibi gelir |
| P3 | iOS Ayarlar → Fenpo → Fotoğraflar **Sınırlı** | PC sınırlı erişimi gösterir, sadece seçilenler listelenir |
| S1 | Fenpo Ayarlar → **Ekran görüntülerini gönder** açık; başka uygulamada ekran görüntüsü al, Fenpo'ya dön | Görüntü PC'ye gider, Aktarımlar'da görünür; aynı görüntü ikinci kez gitmez |
| D1 | Windows'ta Fenpo'da bir klasör paylaş. iPhone'da Dosyalar → Göz At → Konumlar | PC'nin adıyla bir konum; içinde paylaşılan klasör |
| D2 | Bir belgeyi aç; bir resmin küçük resmine bak | Belge iner ve açılır; küçük resimler PC'den gelir |
| D3 | Dosyalar'da bir dosyayı PC klasörüne kopyala; bir dosyanın adını değiştir; bir klasör oluştur; bir dosyayı sil | Hepsi PC'de olur (silinen Geri Dönüşüm Kutusu'nda); aynı ad varsa " (2)" eklenir |
| D4 | PC'de klasöre dosya ekle, iPhone'da klasör açıkken bekle | Birkaç saniye içinde Dosyalar'da görünür (`FolderChanged`) |
| D5 | Eşleştirmeyi kaldır | Dosyalar'daki konum kalkar |
| E1 | PC sekmesi → **Ekranı PC’de göster** → **Yayını Başlat** | PC'de iPhone ekranı görünür (dik), 30 fps civarı; kontrol yok |
| E2 | Denetim Merkezi → Ekran Kaydı'na basılı tut → Fenpo → Yayını Başlat | E1 ile aynı |
| E3 | Windows'tan ekran yansıtmayı başlat (Fenpo kapalıyken de) | iPhone'da "Bilgisayarın ekranını göstermek istiyor" bildirimi; PC "onay bekleniyor" gösterir |
| E4 | Yansıtma sürerken PC'den durdur; iPhone'da kırmızı durum çubuğundan durdur | İkisinde de yayın biter; PC'den durdurunca iPhone "Bilgisayar ekranı göstermeyi durdurdu" der |
| E5 | 10 dakika yansıt | Yayın kendi kendine kesilmez (uzantı bellek sınırı) |

Kayıtta aranacaklar: `[Transfer]`, `[Camera]`, `[LiveActivity]`, `[Actions]`, `[Intents]`, `[Input]`, `[PcNotifications]`, `[Hotspot]`, `[Photos]`, `[Screenshots]`, `[Files]`, `[Screen]`. Uzantılar (Dosyalar, ekran yayını) da aynı kayda yazar. Uygulama ya da bir uzantı çökerse iOS Ayarlar → Gizlilik ve Güvenlik → Analiz ve İyileştirmeler → Analiz Verileri'nden `Fenpo` ile başlayan raporu gönderin.

Geri bildirim kilometre taşı 1'deki gibi: tablo, iPhone kaydı (Geliştirici → Kayıt → Dışa aktar) ve Windows'un kaydı.

## Pano, Fenpo'yu açmadan

Ne beklendiği ve iOS'un neye izin verip vermediği: [CLIPBOARD.md](CLIPBOARD.md). Windows'ta Fenpo açık (tepside de olur) ve **Pano paylaşımı** açık olsun. Her testten önce iPhone'da Fenpo'yu açıp kapatın ki Bluetooth bağlı olsun; Geliştirici → Durum'da **BLE** `ready` görünmeli. Panoyu kontrol etmek için Notlar'da yeni bir nota yapıştırın. Kayıtta `[Clipboard]`, `[Services]`, `[BLE]`, `[AppModel]` satırlarına bakın; kayıt pano içeriğini yazmaz, sadece türünü ve boyunu.

**PC → iPhone**

PC'nin kopyası iPhone'a **sadece Fenpo açıkken** gelir. Build 89'da C2 başarısız oldu ve Buğra bunu kabul etti. C2–C5 ve aşağıdaki tablo, ileride sebebe bakılırsa diye duruyor; bugün beklenen sonuç "gelmez".

| # | Adım | Beklenen | Olmazsa kayıtta neye bakılır |
|---|---|---|---|
| C1 | Fenpo açıkken PC'de bir kelime kopyalayın | Notlar'a aynı kelime yapışır; kayıtta `Clipboard from PC over BLE: text …, app active, … written` | — |
| C2 | Fenpo'dan başka uygulamaya geçin (kapatmayın), 1 dakika bekleyin, PC'de bir kelime kopyalayın, 5 saniye sonra Notlar'a yapıştırın | Build 89: **gelmedi** (beklenen, kabul edildi). Fenpo açıkken Ana Sayfa'da **PC'den yapıştır** onu getirir | Aşağıdaki tablo |
| C3 | iPhone'u kilitleyin, 5 dakika bekleyin, PC'de bir kelime kopyalayın, kilidi açıp Notlar'a yapıştırın | C2 gibi: gelmez | Aşağıdaki tablo |
| C4 | C2'yi bir parola yöneticisinden kopyalanan parolayla yapın | iPhone'a gelir; 2 dakika sonra panodan kendiliğinden silinir, Mac'e geçmez. Kayıtta `…, sensitive` | Windows parola yöneticisinin işaretlediği içeriği hiç göndermiyorsa kayıtta satır olmaz; bu da doğru |
| C5 | Fenpo arka planda, PC'de bir ekran görüntüsünün bir kısmını kopyalayın (Win+Shift+S, 512 KB'tan küçük) | Notlar'a resim yapışır. Kayıtta sırayla `Wi-Fi wanted by PC for Clipboard`, `Wi-Fi channel open for the PC after … s` (10 sn'den az olmalı), `Clipboard item over Wi-Fi … after the PC asked`, `Clipboard from PC over Wi-Fi: image …` | `Wi-Fi channel for the PC failed`: Wi‑Fi yolu kapalı. `open … after 12.0 s`: PC 10 sn bekleyip vazgeçmiş. `Closing Wi-Fi without the clipboard item`: PC göndermedi (Windows kaydı) |
| C6 | Fenpo arka planda, PC'de bir kelime kopyalayın, sonra iPhone'da başka bir şey kopyalayın, Fenpo'yu açın | iPhone'da kopyaladığınız ezilmez | — |

C2/C3 başarısızsa iPhone kaydındaki satırlar hangi durum olduğunu söyler:

| Kayıtta | Anlamı |
|---|---|
| PC'nin kopyası anında hiçbir `[BLE] Received clipboard` satırı yok; Fenpo'yu açınca `Front again after … : BLE connecting` ya da `PC Hello missing` | Bluetooth kanalı askıdan sonra ayakta değildi. Windows kaydında `Clipboard … sent to the phone` satırı olmaz (ya da `Clipboard item not sent` olur) |
| `Front again after … : BLE ready …; over BLE meanwhile: nothing` ve Windows kaydında `Clipboard text sent to the phone` | PC gönderdi ama iOS uygulamayı uyandırıp iletmedi |
| `Dropped a BLE message from the PC: the channel key is not set yet` | Uygulama Bluetooth için yeniden başlatıldı ama anahtar henüz yüklenmemişti |
| `Clipboard from PC over BLE: …, app background; … did not stick` | Mesaj geldi, iOS arka plandaki yazmayı kabul etmedi. Fenpo'yu açınca `Clipboard from PC written on return to the front` gelmeli |
| `… written` ama Notlar'a eski içerik yapışıyor; belki `Pasteboard changed again after the background write` | iOS yazmayı sonradan geri aldı |
| `Restoring 1 peripheral(s): connected, tx notifying` ve sonra `Already subscribed to tx; sending Hello` | iOS Fenpo'yu Bluetooth için yeniden başlattı ve kanal kendiliğinden ayağa kalktı (iyi) |

**iPhone → PC**

| # | Adım | Beklenen |
|---|---|---|
| C7 | Fenpo Ayarlar → **Açılınca panoyu gönder: Sor**. Safari'de bir kelime kopyalayın, Fenpo'yu açın | Ana Sayfa'da "Panoda yeni bir şey var" kartı ve **Yapıştır** düğmesi; iOS izin sormaz. Dokununca PC'nin panosunda aynı kelime, kart kalkar. Kayıtta `Clipboard on open: … : offer` |
| C8 | C7'yi tekrarlayıp kartı **X** ile kapatın, Fenpo'yu kapatıp açın | Aynı pano için kart bir daha çıkmaz |
| C9 | Ayarlar → **Otomatik**. iOS Ayarlar › Uygulamalar › Fenpo › Diğer Uygulamalardan Yapıştır: **Sor**. Bir şey kopyalayıp Fenpo'yu açın | iOS bir kez sorar; İzin Ver'e basınca "Pano PC’ye gitti". Reddet'e basınca "Pano okunamadı …" uyarısı ve aynı pano için bir daha sorulmaz |
| C10 | C9'u Diğer Uygulamalardan Yapıştır: **İzin Ver** ile | Hiç sormadan gider |
| C11 | Otomatik açıkken bir SMS kodunu (ör. 482913) ya da parola yöneticisinden bir parolayı kopyalayıp Fenpo'yu açın | Kendiliğinden gitmez, kart "Parola veya kod olabilir" der. Yapıştır'a basınca PC'ye gider ama PC'nin pano listesinde görünmez |
| C12 | Fotoğraflar'da bir fotoğrafı kopyalayın (Paylaş → Kopyala), Fenpo'yu açıp kartta Yapıştır | PC'nin panosunda resim (512 KB'a sığacak kadar küçültülmüş) |
| C13 | Ayarlar → **Arkaya iki dokun** sayfasındaki adımlarla kestirmeyi kurun. Başka bir uygulamada metin kopyalayıp telefonun arkasına iki kez dokunun | İlk seferde Kestirmeler pano izni ister (Her Zaman İzin Ver). Fenpo açılmadan PC'de aynı metin. Kayıtta `SendTextToPCIntent runs in the app process` |
| C14 | Ayarlar → Eylem Düğmesi → Kestirme → Fenpo → **Panoyu PC’ye gönder** (iPhone 15 Pro ve sonrası); ya da Denetim Merkezi'ne **Panoyu gönder** | Fenpo bir an açılır, pano gider |
| C15 | Notlar'da metin seçip Paylaş → Fenpo → **PC panosuna kopyala**; Safari'de bir sayfayı paylaşıp aynı düğme; Fotoğraflar'da tek fotoğrafla aynı düğme | Üçünde de PC'nin panosunda metin, bağlantı, resim |
| C16 | Fenpo Ayarlar → **Pano paylaşımı** kapalıyken C1 ve C7 | iPhone panosu değişmez (kayıtta `ignored … sharing is off`), kart çıkmaz |
| C17 | C7–C11'i koyu temada ve iPhone dili English iken | Kart, Ayarlar satırı ve Arkaya iki dokun sayfası iki temada ve iki dilde düzgün |

# TestFlight

Yükleme GitHub Actions'tan yapılır, Mac gerekmez. App Store Connect'te uygulama kaydı (`com.fenpo.app`) ve API anahtarı Buğra'da; repo secret'ları `ASC_KEY_ID`, `ASC_ISSUER_ID`, `ASC_KEY_P8`, `APPLE_TEAM_ID`.

## Yüklemek

1. GitHub → **Actions → iOS → Run workflow**: dal `ios`, **Upload a Release build to TestFlight** işaretli.
2. Önce testler koşar (Windows çekirdeği, simülatör, ekran görüntüleri). Geçerlerse **Upload to TestFlight** işi Release arşivini API anahtarıyla imzalar (uygulama, paylaşım uzantısı, widget'lar, App Group) ve yükler.
3. İşin özetinde sürüm ve derleme numarası yazar: `Fenpo 0.1.0 (<run_number>) from <commit>`. Derleme numarası iş akışının çalışma numarasıdır, her yüklemede artar.
4. Build birkaç dakika işlendikten sonra App Store Connect → TestFlight'ta görünür. Dahili test grubuna (ör. "Fenpo ekibi") eklenen testçiler hemen kurabilir.

Şifreleme beyanı Info.plist'te: `ITSAppUsesNonExemptEncryption = NO`. Fenpo sadece Apple'ın Network.framework'ündeki TLS'i ve CryptoKit'teki AES-GCM'i kullanıyor. App Store Connect bu yüzden her build'de soru sormaz.

## Mac'ten yüklemek (yedek)

Actions çalışmazsa, Xcode'u Milvasoft ekibinde oturum açmış bir Mac'te:

```bash
git checkout <commit>
ios/scripts/archive-testflight.sh
```

Burada derleme numarası commit sayısıdır (`git rev-list --count HEAD`). İki yolu karıştırırsanız App Store Connect, daha küçük numaralı build'i reddeder.

## Sorunlar

| Belirti | Ne yapmalı |
|---|---|
| Arşivde `No signing certificate` ya da `Cloud signing permission error` | API anahtarının rolü bulut imzalamaya yetmiyor olabilir. App Store Connect → Kullanıcılar ve Erişim → Entegrasyonlar'da anahtarın rolünü **Admin** yapın ya da "Bulutta yönetilen dağıtım sertifikalarına erişim" iznini verin, sonra yeniden çalıştırın |
| `No profiles for 'com.fenpo.app.share' were found` | Kimlik hesapta kayıtlı (bkz. DECISIONS #26). `-allowProvisioningUpdates` profilleri kendisi oluşturur; yine de olmazsa logu (`testflight-logs`) Buğra'ya gönderin |
| Yükleme `ITMS-91053` (gizlilik bildirimi) uyarısı | `ios/Shared/PrivacyInfo.xcprivacy` eksik bir API nedeni bildiriyor; uyarı metnini Buğra'ya gönderin |
| Yükleme "bundle version must be higher" | Daha büyük numaralı bir build zaten var; iş akışını yeniden çalıştırmak numarayı artırır |
