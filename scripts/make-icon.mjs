import { mkdir, writeFile } from 'node:fs/promises'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import pngToIco from 'png-to-ico'
import sharp from 'sharp'

const root = path.join(path.dirname(fileURLToPath(import.meta.url)), '..')
const src = path.join(root, 'public', 'icon.jpg')
const outDir = path.join(root, 'build')
await mkdir(outDir, { recursive: true })

const png256 = await sharp(src).resize(256, 256, { fit: 'cover' }).png().toBuffer()
const png512 = await sharp(src).resize(512, 512, { fit: 'cover' }).png().toBuffer()
await writeFile(path.join(outDir, 'icon.png'), png256)
await writeFile(path.join(outDir, 'icon-512.png'), png512)

const sizes = [16, 24, 32, 48, 64, 128, 256]
const pngs = await Promise.all(
  sizes.map((size) => sharp(src).resize(size, size, { fit: 'cover' }).png().toBuffer()),
)
await writeFile(path.join(outDir, 'icon.ico'), await pngToIco(pngs))
console.log('icons written to build/')
