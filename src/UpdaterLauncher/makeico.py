# -*- coding: utf-8 -*-
"""把用户给的图标素材转成 Windows 图标，并留一份源图在项目里。

源图在 AppData 的临时目录里，过一阵会被清掉，所以这里复制一份进 src，
以后想换图标直接改 src/UpdaterLauncher/icon-source.png 再跑 build.ps1。
"""
import os
import sys

from PIL import Image

sys.stdout.reconfigure(encoding='utf-8')

SRC = r'C:\Users\hong\AppData\Local\CodeBuddyExtension\Data\74f67999-6d6d-4ee2-9d0c-316bc5473f4d\CodeBuddyIDE\74f67999-6d6d-4ee2-9d0c-316bc5473f4d\history\3f4f2ca63c7b304d3ddb5543a95e66cc\163f46c055f84eaa9a19bd2536d974fd\assets\03_点赞.ab3d59a1b4.png'
HERE = os.path.dirname(os.path.abspath(__file__))

im = Image.open(SRC).convert('RGBA')
print('源图: %s %s' % (im.size, im.mode))

# 存一份源图，避免临时目录被清后无法重建图标
src_copy = os.path.join(HERE, 'icon-source.png')
im.save(src_copy, format='PNG', optimize=True)
print('源图已存: %s (%.1f KB)' % (src_copy, os.path.getsize(src_copy) / 1024))

# ICO 要求正方形；原图已经是 1080x1080，按各尺寸缩放即可
sizes = [(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48),
         (64, 64), (96, 96), (128, 128), (256, 256)]
ico = os.path.join(HERE, 'App.ico')
im.save(ico, format='ICO', sizes=sizes)
print('图标已生成: %s (%.1f KB)  含 %d 个尺寸' % (ico, os.path.getsize(ico) / 1024, len(sizes)))
