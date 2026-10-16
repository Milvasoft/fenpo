# iOS cihaz testleri: kilometre taşı 1 (S4, S6)

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

## 5. Geri bildirim

Buğra'ya şunları gönderin:

1. Commit (`git rev-parse --short HEAD`), Xcode sürümü, iPhone modeli ve iOS sürümü.
2. Aşağıdaki tabloyu doldurun:

   | Test | Sonuç (✓/✗/kısmen) | Not |
   |---|---|---|
   | T1 | | |
   | … | | |
   | T18 | | |

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
