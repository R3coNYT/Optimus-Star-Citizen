# -*- coding: utf-8 -*-
"""Sort les chaines du XAML vers un dictionnaire de ressources.

Chaque litteral devient une cle, et le XAML une reference dynamique. « Dynamique »
et non « statique » : c'est ce qui permet d'echanger le dictionnaire a chaud, sans
reconstruire la fenetre ni redemarrer.
"""
import io
import re
import json
import unicodedata

XAML = 'src/Optimus.App/MainWindow.xaml'
DICT = 'src/Optimus.App/Localization/Strings.fr.xaml'

ATTRS = ('Text', 'Header', 'Content', 'ToolTip')
ATTR_RE = re.compile(r'\b(' + '|'.join(ATTRS) + r')="([^"{][^"]*)"')
RUN_RE = re.compile(r'<Run Text="([^"{][^"]*)"')
SETTER_RE = re.compile(r'(<Setter Property="Text" Value=)"([^"{][^"]*)"')

TABS = {
    'Poste de pilotage': 'Cockpit',
    'Commandes': 'Commands',
    'Touches': 'Keys',
    'Réglages': 'Settings',
    'Macros': 'Macros',
    'Compréhension': 'Understanding',
}

STOP = {'le', 'la', 'les', 'de', 'des', 'du', 'un', 'une', 'et', 'ou', 'a', 'au',
        'aux', 'en', 'que', 'qui', 'ce', 'se', 'sa', 'son', 'ses', 'dans', 'pour',
        'sur', 'par', 'ne', 'pas', 'est', 'nbsp', 'x000d', 'x000a'}

# Cette phrase-la est coupee autour d'une liaison : « Dites « {mot} , ... » pour etre
# entendu. » Une autre langue ne remettrait pas les morceaux dans cet ordre. Elle est
# donc refaite en une seule chaine a trous, plus bas.
SKIP_LINES = {128}


def slug(text):
    plain = unicodedata.normalize('NFKD', text.replace('&#160;', ' '))
    plain = ''.join(c for c in plain if not unicodedata.combining(c))
    words = [w for w in re.findall(r'[A-Za-z0-9]+', plain) if w.lower() not in STOP]
    if not words:
        words = re.findall(r'[A-Za-z0-9]+', plain) or ['Text']
    return ''.join(w[:1].upper() + w[1:] for w in words[:4])


def main():
    raw = io.open(XAML, encoding='utf-8', newline='').read()
    lines = raw.split('\n')

    tab = 'App'
    table = {}          # cle -> texte
    keyed = {}          # texte -> cle, pour ne pas dupliquer une phrase identique
    out = []

    for number, line in enumerate(lines, start=1):
        header = re.search(r'<TabItem Header="([^"]+)"', line)
        if header:
            tab = TABS.get(header.group(1), 'App')

        if number in SKIP_LINES or line.lstrip().startswith('<!--'):
            out.append(line)
            continue

        def translatable(text):
            # Une chaine sans lettre n'a rien a traduire : espaces de calage, points de
            # suite, symboles. XAML ne sait d'ailleurs pas construire un String vide -
            # « Aucun constructeur correspondant sur System.String », constate a l'essai.
            return re.search(r'[A-Za-zÀ-ÿ]{2}', text) is not None

        def key_for(text):
            if text in keyed:
                return keyed[text]
            base = tab + '.' + slug(text)
            key, n = base, 2
            while key in table:
                key = base + str(n)
                n += 1
            table[key] = text
            keyed[text] = key
            return key

        def attr_sub(m):
            if not translatable(m.group(2)):
                return m.group(0)

            return '%s="{DynamicResource %s}"' % (m.group(1), key_for(m.group(2)))

        def run_sub(m):
            if not translatable(m.group(1)):
                return m.group(0)

            return '<Run Text="{DynamicResource %s}"' % key_for(m.group(1))

        def setter_sub(m):
            if not translatable(m.group(2)):
                return m.group(0)

            return '%s"{DynamicResource %s}"' % (m.group(1), key_for(m.group(2)))

        line = SETTER_RE.sub(setter_sub, line)
        line = RUN_RE.sub(run_sub, line)
        line = ATTR_RE.sub(attr_sub, line)
        out.append(line)

    io.open(XAML, 'w', encoding='utf-8', newline='').write('\n'.join(out))

    header = [
        '<!--',
        '  Les mots de l\'interface, en francais.',
        '',
        '  Un dictionnaire de ressources plutot qu\'un fichier .resx : WPF sait en echanger un',
        '  a chaud, ce qu\'un ResourceManager ne fait pas. C\'est ce qui permet de changer de',
        '  langue sans reconstruire la fenetre ni redemarrer - et le pilote doit pouvoir',
        '  revenir en arriere s\'il s\'est trompe de langue, sans avoir a la relire dans une',
        '  langue qu\'il ne lit pas.',
        '',
        '  Les cles portent l\'onglet ou la chaine apparait : c\'est ce qui rend une traduction',
        '  relisable dans l\'ordre de l\'ecran, plutot qu\'en vrac alphabetique.',
        '',
        '  Ce fichier est engendre une premiere fois par tools/extract-strings.py, puis tenu',
        '  a la main.',
        '-->',
        '<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"',
        '                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"',
        '                    xmlns:sys="clr-namespace:System;assembly=mscorlib">',
        '',
    ]

    body = []
    current = None
    for key in sorted(table, key=lambda k: (list(TABS.values()) + ['App']).index(k.split('.')[0])
                      if k.split('.')[0] in list(TABS.values()) + ['App'] else 99):
        section = key.split('.')[0]
        if section != current:
            current = section
            body.append('  <!-- ================= %s ================= -->' % section)
        raw_value = table[key]
        value = raw_value.replace('&', '&amp;').replace('&amp;#160;', '&#160;')
        space = ' xml:space="preserve"' if raw_value != raw_value.strip() else ''
        body.append('  <sys:String x:Key="%s"%s>%s</sys:String>' % (key, space, value))

    io.open(DICT, 'w', encoding='utf-8', newline='').write(
        '\n'.join(header + body + ['', '</ResourceDictionary>', '']))

    io.open('table.json', 'w', encoding='utf-8').write(
        json.dumps(table, ensure_ascii=False, indent=1))

    print(len(table), 'cles ecrites dans', DICT)


main()
