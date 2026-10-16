# S4 (Windows tarafı): PC'nin Fenpo BLE çevre birimi olması

## Hedef

S4'ün iPhone tarafı (arka planda uyanma, AccessorySetupKit, zorla kapatma sonrası) için PC'de karşı ucu kurmak: sabit Fenpo GATT servisi, `rx` ve `tx` karakteristikleri, reklam. Ayrıca Windows reklamına PC'yi ayırt edecek bir şeyin sığıp sığmadığını ölçmek (iPhone oturumunun sorusu: birden fazla Fenpo PC'si varsa ASK seçicisinde hangisi hangisi).

UUID'ler iPhone oturumunun `protocol: fixed Fenpo BLE service and characteristic UUIDs` commit'inden (`beede75`, `ios` dalı; henüz `main`'de değil):

| Öğe | UUID | Özellik |
|---|---|---|
| Servis | `1888047E-C8C5-45CC-AAEA-333A8859E79F` | Birincil, reklamda |
| `rx` | `ECC8D0D1-990A-4B02-A4FC-49FA647E7254` | Yanıtsız yazma, şifreleme gerekli |
| `tx` | `E9EC01E0-8C02-4B8E-902C-4F113E25DFC8` | Bildirim, şifreleme gerekli |

## Yapılanlar

`PeripheralSpike/`: paketlenmemiş .NET 10 konsol uygulaması. `GattServiceProvider` ile servisi ve iki karakteristiği kurar, bağlanabilir ve keşfedilebilir reklam başlatır. `rx` yazmalarını ve `tx` aboneliklerini loglar; `send <metin>` ile `tx`'ten bildirim gönderir.

```bash
dotnet run --project spikes/S4-ble-peripheral/PeripheralSpike
dotnet run --project spikes/S4-ble-peripheral/PeripheralSpike -- --probe
```

Komutlar: `adv <0|4|8|20>` (servis verisi uzunluğu), `send <metin>`, `status`, `q`.

## Sonuç (16 Ekim 2026, MediaTek adaptör, Windows 11 26H2)

### Bu makinede, telefon olmadan ölçülenler

- Paketlenmemiş bir süreçten `GattServiceProvider.CreateAsync`, karakteristikler ve reklam çalışıyor. Paket kimliği gerekmiyor.
- Sadece servis UUID'siyle reklam: `Started`.
- **Servis verisi eklenince**, 4 bayt bile olsa (4, 8, 13, 20, 40 denendi), durum **`StartedWithoutAllAdvertisementData`** oluyor. Windows reklamı başlatıyor ama servis verisini koymuyor. Adaptör genişletilmiş reklamı desteklediği halde (`MaxAdvertisementDataLength` 312) `GattServiceProvider` eski 31 baytlık reklamda kalıyor. Bayraklar (3) ve 128 bit UUID (18), 128 bit UUID'li servis verisine yer bırakmıyor.
- Her `StartAdvertising` önce kısa bir `Aborted` olayı, hemen ardından asıl durumu veriyor. Ürün kodu ilk `Aborted`'ı hata saymamalı.

### iPhone'la denenmesi gerekenler (Buğra, sabah)

Fenpo iOS uygulaması henüz cihazda olmadığı için genel amaçlı bir BLE uygulamasıyla bakılacak:

1. iPhone'a App Store'dan **nRF Connect for Mobile**'ı (Nordic Semiconductor, ücretsiz) kur.
2. PC'de spike'ı başlat (bana "S4'ü başlat" demen yeter).
3. nRF Connect > **Scanner**: listede `1888047E…` servisini yayınlayan cihazı bul. **Hangi adla göründüğünü** (BUGRAKOSEN mi, isimsiz mi) ve RSSI'yi not et. Cihaza dokunup reklam verisinin ham halini gösteren ekranın ekran görüntüsünü al.
4. **Connect**. Fenpo servisini aç; `E9EC01E0…` (tx) satırındaki **aşağı ok** ile bildirimlere abone ol. iPhone eşleşme ya da izin sorarsa ne dediğini yaz.
5. `ECC8D0D1…` (rx) satırındaki **yukarı ok** ile bir metin yaz (ör. `merhaba`), gönder.
6. Ben PC'den `send selam` göndereceğim. nRF Connect'te tx değerinin değiştiğini görmelisin.

Gözlenecekler: reklamdaki ad, şifreli karakteristiğe ilk erişimde iOS'un ne yaptığı (S1'de bağ zaten kurulu olduğu için soru çıkmayabilir), PC loglarında abonelik ve yazmalar, MTU.

## Karar (şimdilik)

- PC'yi ayırt etmek için **reklamdaki servis verisi kullanılamaz** (`GattServiceProvider` buna izin vermiyor). Kalan seçenekler:
  1. Reklamdaki yerel ad. Windows'un bunu kendiliğinden koyup koymadığını nRF Connect gösterecek.
  2. Ayrı bir `BluetoothLEAdvertisementPublisher` ile üretici verisi. Ama bu ayrı bir reklam seti; iPhone'un iki reklamı aynı PC'ye bağlayabileceği belli değil.
  3. Bağlandıktan sonra şifreli bir karakteristikten `pc_id` okumak. Eşleşmeyi gerektirir ama her durumda çalışır.
- iPhone oturumunun görüşü: ASK'ın `ASDiscoveryDescriptor`'ı servis UUID'sine ek olarak `bluetoothNameSubstring` ile de süzebiliyor. Reklamda yerel ad varsa 1. seçenek en temizi (ad QR'daki `n` alanından gelir). Ad yoksa 3. seçenek: telefon bağlanınca info karakteristiğinden `pc_id`'yi okur, eşleşmedeki `pc_id`'yle tutmazsa bağlantıyı bırakır ve kullanıcıya söyler. 2. seçenek tercih edilmiyor.
- Seçim nRF Connect sonucundan ve iOS tarafında ASK seçicisinin neyi gösterdiğinden sonra yapılır (iPhone oturumu bunu kilometre taşı 1 test listesine ekledi). Protokole bir "info" karakteristiği eklenirse ayrı bir `protocol:` commit'i olur ve Windows oturumu atar.

## Ürün koduna etkisi

1. `Fenpo.Bluetooth` içinde `FenpoGattServer`: servis, `rx`/`tx`, reklam; durum olaylarında ilk `Aborted`'ı yok sayar.
2. `IsPeripheralRoleSupported` false ise ③ kanalı kapanır ve ayarlarda açıklanır (S0).
3. PC kimliği reklamda taşınmaz (en azından servis verisinde); seçim yukarıdaki kararla netleşir.
