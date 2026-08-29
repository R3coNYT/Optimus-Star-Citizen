# -*- coding: utf-8 -*-
"""Verifie que toute cle employee par le code existe dans le dictionnaire.

Une cle manquante ne casse pas la compilation : elle s'affiche telle quelle a
l'ecran, ce qui ne se voit que si l'on regarde le bon onglet au bon moment.
"""
import io
import os
import re
import glob

SEP = os.sep


def sources():
    for path in glob.glob('src/Optimus.App/**/*.cs', recursive=True):
        if 'obj' + SEP in path or SEP + 'obj' in path:
            continue
        yield path


def main():
    used = set()

    for path in sources():
        text = io.open(path, encoding='utf-8').read()

        # T("Cle") et T("Cle", ...)
        used |= set(re.findall(r'Localizer\.T\(\s*"([\w.]+)"', text))

        # T(condition ? "CleA" : "CleB")
        for a, b in re.findall(r'T\(\s*[^;]*?\?\s*"([\w.]+)"\s*:\s*"([\w.]+)"', text, re.S):
            used.add(a)
            used.add(b)

    declared = set(re.findall(
        r'x:Key="([^"]+)"',
        io.open('src/Optimus.App/Localization/Strings.fr.xaml', encoding='utf-8').read()))

    missing = sorted(k for k in used if k not in declared)

    print(len(used), 'cles employees par le code,', len(declared), 'declarees')
    print('manquantes :', ', '.join(missing) if missing else 'aucune')


main()
