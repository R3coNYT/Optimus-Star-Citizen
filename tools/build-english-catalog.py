# -*- coding: utf-8 -*-
"""Engendre le catalogue anglais a partir du francais.

Seuls les mots changent. Les identifiants de commande, les action_id du jeu, les
temporisations, les exigences et les parametres sont recopies tels quels : ce sont
eux qui font marcher la chose, et une divergence entre les deux fichiers ne se
verrait qu'au moment ou une touche ne partirait pas.

Le vocabulaire suit celui du JEU, qui n'existe qu'en anglais : « landing gear »,
« quantum drive », « master mode », « gimbals », « chaff », « visor wipe ». Traduire
depuis le francais aurait produit des tournures qu'aucun pilote ne prononce.
"""
import io
import json
import collections

# id -> (nom affiche, formulations, formulations ON, formulations OFF)
T = {
 'ship.power.toggle': ('Main power', ['power', 'main power'],
   ['power on the ship', 'power up', 'turn on power'],
   ['power down the ship', 'power down', 'turn off power', 'cut the power']),

 'ship.engines.toggle': ('Engines', ['engines', 'thrusters'],
   ['engines on', 'start the engines', 'turn on the engines'],
   ['engines off', 'shut down the engines', 'kill the engines', 'stop the engines']),

 'ship.shields.toggle': ('Shields', ['shields'],
   ['shields up', 'raise the shields', 'turn on the shields'],
   ['shields down', 'drop the shields', 'turn off the shields', 'lower the shields']),

 'ship.weapons.toggle': ('Weapons', ['weapons', 'guns'],
   ['weapons hot', 'arm the weapons', 'turn on the weapons'],
   ['weapons cold', 'stow the weapons', 'turn off the weapons', 'disarm the weapons']),

 'ship.flightready': ('Flight ready',
   ['flight ready', 'ready for flight', 'prep the ship', 'startup sequence'], [], []),

 'ship.lights.toggle': ('Ship lights', ['lights', 'the lights', 'headlights'],
   ['lights on', 'turn on the lights', 'headlights on'],
   ['lights off', 'turn off the lights', 'kill the lights', 'headlights off']),

 'ship.doors.toggle': ('Ship doors', ['the doors'],
   ['open the doors', 'doors open', 'open up'],
   ['close the doors', 'doors closed', 'shut the doors']),

 'ship.doorlocks.toggle': ('Door locks', ['door locks'],
   ['lock the doors', 'lock everything'],
   ['unlock the doors', 'unlock everything']),

 'ship.portlocks.toggle': ('Port locks', ['port locks'],
   ['lock the ports'], ['unlock the ports']),

 'ship.self_destruct': ('Self destruct',
   ['self destruct', 'blow the ship', 'trigger self destruct'], [], []),

 'ship.eject': ('Eject', ['eject', 'eject me', 'punch out'], [], []),

 'ship.emergency_exit': ('Emergency exit', ['emergency exit', 'get me out'], [], []),

 'flight.landing_gear.toggle': ('Landing gear', ['landing gear', 'the gear'],
   ['gear down', 'lower the gear', 'deploy the gear'],
   ['gear up', 'raise the gear', 'retract the gear']),

 'flight.autoland': ('Autoland',
   ['autoland', 'auto landing', 'land the ship', 'put us down'], [], []),

 'flight.atc_request': ('ATC request',
   ['request clearance', 'call atc', 'hail the tower', 'contact the tower',
    'request landing', 'landing clearance', 'request takeoff', 'takeoff clearance'], [], []),

 'flight.docking_request': ('Docking request',
   ['request docking', 'docking', 'dock us'], [], []),

 'flight.decoupled.toggle': ('Decoupled mode', ['decoupled', 'decoupled mode'],
   ['go decoupled', 'decouple'],
   ['recouple', 'go coupled', 'back to coupled']),

 'flight.space_brake': ('Space brake', ['brake', 'space brake', 'stop', 'all stop'], [], []),

 'flight.vtol.toggle': ('VTOL', ['vtol', 'vtol mode'],
   ['vtol on', 'deploy the nacelles', 'swing the nacelles'],
   ['vtol off', 'retract the nacelles']),

 'flight.speed_limiter.up': ('Raise the speed limiter',
   ['speed up', 'raise the limiter', 'faster'], [], []),

 'flight.speed_limiter.down': ('Lower the speed limiter',
   ['slow down', 'lower the limiter', 'slower', 'ease off'], [], []),

 'nav.master_mode.cycle': ('Flight mode', ['switch mode', 'flight mode'],
   ['combat mode', 'go to combat', 'scm mode', 'go to scm'],
   ['nav mode', 'go to nav', 'navigation mode', 'back to nav']),

 'quantum.engage': ('Engage quantum',
   ['engage quantum', 'quantum jump', 'spool the quantum', 'quantum travel', 'quantum'],
   [], []),

 'flight.jump_request': ('Jump point',
   ['engage the jump point', 'jump point', 'take the jump point'], [], []),

 'power.engines.increase': ('More engine power',
   ['more power to engines', 'engine priority', 'give me thrust'], [], []),

 'power.engines.decrease': ('Less engine power',
   ['less power to engines', 'cut engine power'], [], []),

 'power.shields.increase': ('More shield power',
   ['more power to shields', 'shield priority', 'harden the shielding'], [], []),

 'power.shields.decrease': ('Less shield power',
   ['less power to shields', 'cut shield power'], [], []),

 'power.weapons.increase': ('More weapon power',
   ['more power to weapons', 'weapon priority', 'charge the capacitors'], [], []),

 'power.weapons.decrease': ('Less weapon power',
   ['less power to weapons', 'cut weapon power'], [], []),

 'power.reset': ('Balanced power',
   ['balance the power', 'even power', 'reset the power'], [], []),

 'power.throttle.max': ('Maximum power',
   ['maximum power', 'full power', 'all power'], [], []),

 'power.throttle.min': ('Minimum power',
   ['minimum power', 'cut all power', 'idle power'], [], []),

 'shields.raise.front': ('Shields forward',
   ['shields forward', 'shields to the front', 'reinforce the front', 'cover the front'],
   [], []),

 'shields.raise.back': ('Shields aft',
   ['shields aft', 'shields to the rear', 'reinforce the rear', 'cover our back'], [], []),

 'shields.raise.left': ('Shields to port',
   ['shields left', 'shields to port', 'reinforce the left'], [], []),

 'shields.raise.right': ('Shields to starboard',
   ['shields right', 'shields to starboard', 'reinforce the right'], [], []),

 'shields.reset': ('Balanced shields',
   ['balance the shields', 'even shields', 'spread the shields'], [], []),

 'combat.gimbal.toggle': ('Gimbals', ['gimbals', 'toggle the gimbals'],
   ['gimbals on'], ['gimbals off', 'gimbal lock']),

 'combat.countermeasure.decoy': ('Decoy',
   ['decoy', 'launch a decoy', 'countermeasures', 'flares'], [], []),

 'combat.countermeasure.noise': ('Noise', ['noise', 'chaff', 'launch chaff', 'jam them'],
   [], []),

 'targeting.auto.toggle': ('Auto targeting', ['auto targeting', 'automatic targeting'],
   ['auto targeting on', 'automatic lock'],
   ['auto targeting off', 'kill the auto targeting']),

 'targeting.unlock': ('Unlock target',
   ['unlock the target', 'drop the target', 'break the lock'], [], []),

 'targeting.remove_pins': ('Remove pinned targets',
   ['remove the pins', 'clear the targets', 'clean the targets'], [], []),

 'scan.mode.toggle': ('Scanning mode', ['scan mode', 'scanner'],
   ['scanning on', 'go to scan'],
   ['scanning off', 'leave scan', 'exit scan', 'kill the scan']),

 'scan.ping': ('Radar ping', ['ping', 'send a ping', 'sweep the area', 'probe'], [], []),

 'mining.mode.toggle': ('Mining mode', ['mining mode'],
   ['mining on', 'go to mining'],
   ['mining off', 'leave mining', 'exit mining']),

 'salvage.mode.toggle': ('Salvage mode', ['salvage mode', 'salvage'],
   ['salvage on', 'go to salvage'],
   ['salvage off', 'leave salvage']),

 'view.cycle': ('Change view', ['change view', 'next view', 'camera'], [], []),

 'view.look_behind': ('Look behind',
   ['look behind', 'rear view', "what's behind us"], [], []),

 'ui.mobiglas': ('mobiGlas', ['mobiglas', 'open the mobiglas', 'my mobiglas'], [], []),

 'ui.starmap': ('Starmap', ['starmap', 'open the map', 'star map'], [], []),

 'hud.visor_wipe': ('Visor wipe', ['wipe the visor', 'clean the visor', 'visor'], [], []),

 'system.status': ('System report',
   ['system report', 'systems status', 'give me a report', 'status'], [], []),

 'system.simulation.toggle': ('Toggle simulation mode', ['simulation mode'],
   ['simulation on'], ['simulation off', 'turn off simulation']),

 'system.mute': ('Be quiet', ['be quiet', 'silence', 'not a word', 'hush'], [], []),

 'system.repeat': ('Repeat', ['repeat', 'what did you say', 'say that again'], [], []),

 'system.cancel': ('Cancel', ['cancel', 'forget it', 'never mind', 'belay that'], [], []),

 'dialogue.identity': ('Who are you',
   ['who are you', 'introduce yourself', "what's your name", 'what are you called',
    'who am i talking to'], [], []),

 'dialogue.wellbeing': ('How are you',
   ['how are you', 'you okay', 'are you alright', 'everything fine'], [], []),

 'system.confirm': ('Confirm',
   ['yes', 'confirm', 'go ahead', 'affirmative', 'i confirm', 'correct'], [], []),

 'system.deny': ('Refuse', ['no', 'negative', 'absolutely not', 'do nothing'], [], []),

 'dialogue.acknowledge': ('Acknowledgement',
   ['thanks', 'well done', 'perfect', 'nicely done'], [], []),

 'dialogue.reaction': ('Reaction to an event',
   ['did you see that', 'what was that', 'that was close', "it's getting hot"], [], []),

 'macro.preflight': ('Pre-flight sequence',
   ['prep for takeoff', 'takeoff procedure', 'full startup'], [], []),

 'macro.landing': ('Landing procedure',
   ['landing procedure', 'prep the landing', "we're setting down", 'landing sequence'],
   [], []),

 'macro.battle_stations': ('Battle stations',
   ['battle stations', 'ready for combat', 'prep for combat', "we're going to fight",
    'red alert'], [], []),

 'macro.shutdown': ('Ship shutdown',
   ['shut everything down', 'full shutdown', 'shutdown procedure'], [], []),
}


def main():
    source = 'data/commands/starcitizen.core.json'
    target = 'data/commands/starcitizen.core.en.json'

    d = json.load(io.open(source, encoding='utf-8'),
                  object_pairs_hook=collections.OrderedDict)

    d['locale'] = 'en-US'
    d['name'] = 'Star Citizen - core commands'
    d['notes'] = [
        "Engendre depuis starcitizen.core.json : seuls les mots changent. Les identifiants,",
        "les action_id, les temporisations, les exigences et les parametres en sont la copie",
        "exacte - une divergence ne se verrait qu'au moment ou une touche ne partirait pas.",
        "Le vocabulaire suit celui du jeu, qui n'existe qu'en anglais.",
    ]

    missing = []

    for command in d['commands']:
        key = command['id']
        if key not in T:
            missing.append(key)
            continue

        name, phrases, on, off = T[key]
        command['name'] = name
        command['voice_phrases'] = phrases

        # Les listes ON/OFF n'existent que la ou le francais en avait : les ajouter
        # ailleurs inventerait une polarite que le catalogue ne porte pas.
        if 'phrases_on' in command:
            assert on, 'polarite ON manquante pour ' + key
            command['phrases_on'] = on
        if 'phrases_off' in command:
            assert off, 'polarite OFF manquante pour ' + key
            command['phrases_off'] = off

    assert not missing, 'commandes non traduites : ' + ', '.join(missing)

    io.open(target, 'w', encoding='utf-8', newline='\n').write(
        json.dumps(d, ensure_ascii=False, indent=2) + '\n')

    total = sum(len(c['voice_phrases']) + len(c.get('phrases_on', []))
                + len(c.get('phrases_off', [])) for c in d['commands'])
    print(len(d['commands']), 'commandes,', total, 'formulations')


main()
