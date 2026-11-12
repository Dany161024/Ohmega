pkgname=ohmega
pkgver=1.3.1
pkgrel=1
pkgdesc=Ohmega normalizes the loudness of your audio files using the EBU R128 standard.
arch=('any')
url=https://ohmega.is-my.app/
license=('GPL-3.0-only')

depends=('python' 'python-pyqt6' 'ffmpeg')

source=("git+https://github.com/Dany161024/Ohmega.git")
sha256sums=('SKIP')

package() {
	cd "$srcdir/Ohmega"

	install -Dm644 main.py "$pkgdir/usr/share/$pkgname/main.py"
	install -Dm644 ohmega_core.py "$pkgdir/usr/share/$pkgname/ohmega_core.py"
	install -Dm644 cli.py "$pkgdir/usr/share/$pkgname/cli.py"
	install -Dm644 ohmega.png "$pkgdir/usr/share/$pkgname/ohmega.png"

	install -Dm755 scripts/launcher.sh "$pkgdir/usr/bin/ohmega"
	install -Dm755 scripts/cli-launcher.sh "$pkgdir/usr/bin/ohmega-cli"
}