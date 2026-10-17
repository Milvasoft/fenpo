# Lisanslar

Bu klasördeki görsellerde ve videolarda kullanılan, Fenpo'ya ait olmayan her şey.

## Müzik (yalnızca `fenpo-preview-music.mp4`)

| | |
|---|---|
| Parça | "Digital Clouds" |
| Sanatçı | Alejandro Magaña (A. M.) |
| Kaynak | Mixkit, https://mixkit.co/free-stock-music/tag/technology/ (dosya: https://assets.mixkit.co/music/623/623.mp3) |
| Lisans | Mixkit Stock Music Free License, https://mixkit.co/license/#musicFree |
| İndirildiği tarih | 18 Ekim 2026 (Buğra'nın onayıyla) |

Lisans, parçanın atıf gerekmeden çevrimiçi pazarlama ve reklam videolarında kullanılmasına ve düzenlenmesine izin
veriyor. Parçanın yalnızca başı kesilip baştan ve sondan yumuşatılıyor (`scripts/build.py`); başka bir parçayla
karıştırılmıyor ve tek başına dağıtılmıyor. Dosya repoya girmez; her üretimde kaynağından indirilir.

Müziksiz sürüm (`fenpo-preview.mp4`) yalnızca sessiz bir stereo iz taşır.

## Yazı tipi

Başlıklar ve alt satırlar SF Pro ile yazılır: CI'daki macOS'un sistem yazı tipi
(`/System/Library/Fonts/SFNS.ttf`), uygulamanın kendi arayüzünde de kullandığı yazı tipi. Apple'ın SF Pro lisansı,
yazı tipinin Apple platformlarında çalışan yazılımların arayüz görsellerinde kullanılmasına izin veriyor
(https://developer.apple.com/fonts/). Yazı tipi dosyası repoya girmez. Windows'ta yerel denemelerde Segoe UI kullanılır;
mağazaya giden görseller CI'da SF Pro ile üretilir.

## Fenpo'ya ait olanlar

- Ekran görüntülerindeki arayüz Fenpo for iPhone'un kendisi; simgeler SF Symbols (uygulamanın arayüzünde, Apple'ın
  kurallarına göre).
- Uygulama simgesi ve "Çekirdek" logosu Fenpo'nun (`design/logo`).
- Ekranlardaki içerik uydurma demo verisi: "Ofis PC" / "Office PC", dosya, şarkı ve sanatçı adları hayali; gerçek bir kişi,
  marka ya da hesap yok.
- Renkler Fenpo'nun tasarım sözlüğünden (CLAUDE.md).
