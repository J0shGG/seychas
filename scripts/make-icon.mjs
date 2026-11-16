import { copyFile, mkdir, writeFile } from 'node:fs/promises'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import pngToIco from 'png-to-ico'
import sharp from 'sharp'

const root = path.join(path.dirname(fileURLToPath(import.meta.url)), '..')
const src = path.join(root, 'public', 'logo.svg')
const traySrc = path.join(root, 'public', 'tray.svg')
const outDir = path.join(root, 'build')
await mkdir(outDir, { recursive: true })

const png256 = await sharp(src).resize(256, 256).png().toBuffer()
const png512 = await sharp(src).resize(512, 512).png().toBuffer()
await writeFile(path.join(outDir, 'icon.png'), png256)
await writeFile(path.join(outDir, 'icon-512.png'), png512)
await sharp(src).resize(512, 512).jpeg({ quality: 92 }).toFile(path.join(root, 'public', 'icon.jpg'))
await sharp(src).resize(512, 512).jpeg({ quality: 92 }).toFile(path.join(root, 'src', 'assets', 'icon.jpg'))
await copyFile(src, path.join(root, 'public', 'favicon.svg'))

const sizes = [16, 24, 32, 48, 64, 128, 256]
const pngs = await Promise.all(sizes.map((size) => sharp(src).resize(size, size).png().toBuffer()))
await writeFile(path.join(outDir, 'icon.ico'), await pngToIco(pngs))
await sharp(traySrc).resize(32, 32).png().toFile(path.join(root, 'electron', 'tray.png'))
console.log('icons written to build/')
