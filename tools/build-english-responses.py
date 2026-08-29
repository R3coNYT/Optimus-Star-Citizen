# -*- coding: utf-8 -*-
"""Engendre les repliques anglaises a partir des francaises.

Meme structure, memes entrees, memes evenements, memes « requires » : seul le texte
change. Les marqueurs {pilote} et {command} restent tels quels — c'est le composeur
qui les remplace, et il les cherche litteralement.

Le registre est celui du copilote livre : militaire, calme, professionnel. Un
copilote anglais qui plaisanterait la ou le francais reste sobre ne serait pas une
traduction, ce serait un autre personnage.
"""
import io
import json
import collections

EN = {
 'system.success': {'success': [
   'Received.', 'Done.', 'Executed.', 'At your orders, {pilote}.', 'Affirmative.',
   'It is done.', 'Copy that.', 'Command sent.', 'Understood, {pilote}.', 'Compliant.']},

 'system.repeated_failure': {'fail': [
   "Third failure. The shortcut for {command} is not configured.",
   "This will not go through until {command} has a key assigned.",
   "{command} fails every time. Check its configuration.",
   "Still no key assigned.",
   "Same cause, {pilote}. Missing key.",
   "Nothing has changed. Shortcut absent."]},

 'macro.preflight.begin': {'any': [
   'Startup sequence. Hands off.', 'Preparing for takeoff, {pilote}.']},
 'macro.preflight.done': {'any': [
   'Ship ready, clearance requested.',
   'Systems online. Calling the tower, {pilote}.',
   'Ready for takeoff. She is yours.']},
 'macro.landing.begin': {'any': [
   'On approach. Lowering the gear.', 'Landing configuration, {pilote}.']},
 'macro.landing.done': {'any': [
   'Clearance requested. Ready to set down.', 'We are ready, {pilote}.']},
 'macro.battle.begin': {'any': ['Battle stations.', 'Arming up, {pilote}.']},
 'macro.battle.done': {'any': ['Ready for combat.', 'Everything is hot, {pilote}.']},
 'macro.shutdown.begin': {'any': ['Shutting the systems down.', 'Cutting everything, {pilote}.']},
 'macro.shutdown.done': {'any': ['Ship powered down. Good day.', 'Everything is off, {pilote}.']},

 'copilot.switch': {'any': [
   'I leave you with {command}.', '{command} is taking over.',
   'Handing over. {command} is listening.', 'Over to {command}, then. Safe flight.']},

 'bindings.profile': {'any': [
   '{command}. Keys updated.', 'Profile loaded, {pilote}.', '{command} in effect.',
   'Keys switched.', '{command}. Check the game is on the same layout.']},

 'system.already_in_state': {'any': [
   'That is already the case, {pilote}.', '{command}: nothing to change.',
   'Nothing to change.', 'Already done.',
   'I believed it was already so. Ask again if I am wrong.']},

 'system.game_detected': {'any': [
   'Star Citizen detected. Taking my station.',
   'Ship online. I am listening, {pilote}.']},
 'system.game_closed': {'any': [
   'The game has closed. Standing by.', 'Back at the dock. Going on standby.']},

 'system.unknown_command': {'unknown': [
   'I do not know that command.',
   'Negative. That instruction is not in my protocols.',
   'I did not understand, {pilote}.',
   'Say that again, please.']},

 'system.no_binding': {'fail': [
   'The command exists, but no shortcut is configured.',
   'Negative. No key is assigned to that action.',
   'Action recognised, shortcut missing.',
   '{command} has no key. I cannot act.',
   'I know {command}, but the game has assigned it no key.',
   'Shortcut absent, {pilote}.']},

 'system.game_not_running': {'fail': [
   'Star Citizen is not running, {pilote}.',
   'Negative. No ship to fly at the moment.']},
 'system.game_not_foreground': {'fail': [
   'Understood, but the game is not in the foreground.',
   'I am waiting for you to come back to the cockpit.']},
 'system.kill_switch': {'fail': [
   'Commands locked. Emergency stop is active.',
   'Negative. I am in safe mode, {pilote}.']},

 'system.needs_confirmation': {'clarify': [
   'Do you confirm?', 'This action cannot be undone. Confirm.',
   'Awaiting your confirmation, {pilote}.']},
 'system.clarify': {'clarify': [
   'Do you mean {command}?', 'Be more precise, {pilote}.',
   'Several commands match.']},
 'system.failed': {'fail': [
   'Execution failed.', 'Negative. The sequence did not go through.',
   'Something blocked, {pilote}.']},

 'ship.lights.on': {'success': [
   'Lights on.', 'Let there be light.', 'Lights up, {pilote}.', 'There is your light.']},
 'ship.lights.off': {'success': [
   'Lights out.', 'Lights cut.', 'Lighting off, {pilote}.', 'Into the dark we go.']},
 'ship.shields.on': {'success': [
   'Shields up.', 'Protection active, {pilote}.', 'Shields online.']},
 'ship.shields.off': {'success': [
   'Shields down.', 'We are exposed, {pilote}.', 'Protection disabled.']},
 'ship.engines.on': {'success': [
   'Thrusters online.', 'Thrust available, {pilote}.', 'Engines running.']},
 'ship.engines.off': {'success': [
   'Thrusters cut.', 'Engines off, {pilote}.', 'No more thrust.']},
 'ship.weapons.on': {'success': ['Weapons online.', 'Armament active, {pilote}.']},
 'ship.weapons.off': {'success': ['Weapons cut.', 'Armament stowed, {pilote}.']},
 'ship.power.on': {'success': ['Ship powered up.', 'Circuits live, {pilote}.']},
 'ship.power.off': {'success': ['Power cut.', 'Ship powered down, {pilote}.']},
 'ship.doors.on': {'success': [
   'Doors open, {pilote}.', 'Compartments unlocked.', 'Airlocks open.']},
 'ship.doors.off': {'success': [
   'Doors closed.', 'Compartments locked, {pilote}.', 'Airlocks sealed.']},

 'flight.landing_gear.on': {'success': ['Gear down.', 'Landing gear deployed, {pilote}.']},
 'flight.landing_gear.off': {'success': ['Gear up.', 'Gear retracted, {pilote}.']},
 'scan.mode.on': {'success': ['Scanning mode active.', 'Sensors on active watch.']},
 'scan.mode.off': {'success': ['Leaving scanning mode.', 'Sensors at rest, {pilote}.']},

 'ship.doors.toggle': {'success': [
   'Doors open, {pilote}.', 'Compartments unlocked.', 'Airlocks actuated.',
   'There. Try not to fall out.']},
 'ship.lights.toggle': {'success': [
   'Lighting switched.', 'Running lights.', 'Lights, {pilote}.',
   'Lighting.', 'Lights switched.', 'There is your light.']},

 'nav.master_mode.combat': {'success': [
   'Combat mode. Ready.', 'Weapons hot, {pilote}.', 'Combat configuration.']},
 'nav.master_mode.calm': {'success': [
   'Back to navigation.', 'Travel mode, {pilote}.', 'Systems in cruise configuration.']},

 'ship.engines.toggle': {'success': [
   'Thrusters switched.', 'Engines, {pilote}.', 'Thrust available.']},
 'ship.power.toggle': {'success': [
   'Main power switched.', 'Reactor, {pilote}.', 'Circuits live.']},
 'ship.flightready': {'success': [
   'Startup sequence engaged.', 'Ship ready for takeoff, {pilote}.', 'All systems online.']},
 'ship.self_destruct': {'success': [
   'Self destruct armed. It would be a good time to leave.',
   'Countdown running, {pilote}.']},

 'quantum.engage': {'success': [
   'Trajectory calculated. Hold on, {pilote}.', 'Quantum jump engaged.',
   'Vector locked. Departing.', 'Quantum.',
   'Course plotted. On our way, {pilote}.', 'Warp engaged.',
   'I hope you know where we are going.']},

 'nav.master_mode.cycle': {'success': ['Flight mode switched.', 'Switched, {pilote}.']},
 'flight.landing_gear.toggle': {'success': ['Landing gear.', 'Gear switched, {pilote}.']},
 'flight.autoland': {'success': ['Automatic approach engaged.', 'Setting us down, {pilote}.']},
 'scan.mode.toggle': {'success': ['Scanning mode.', 'Sensors on active watch.']},
 'scan.ping': {'success': ['Ping sent.', 'Sweeping.']},
 'combat.countermeasure.decoy': {'success': ['Decoy launched.', 'Countermeasures deployed.']},
 'shields.raise.front': {'success': [
   'Shields reinforced forward.', 'Frontal protection, {pilote}.']},
 'power.reset': {'success': ['Neutral distribution restored.', 'Power balanced.']},

 'system.status': {'any': [
   'All systems are operational, {pilote}.',
   'Parameters nominal. No anomaly detected.',
   'Reactor nominal, shields at one hundred percent, nothing to report.']},
 'system.mute': {'any': ['Understood. Radio silence.', 'I will be quiet, {pilote}.']},
 'system.cancel': {'any': ['Cancelled.', 'As you wish.']},

 'dialogue.identity': {'any': [
   'Optimus, your onboard copilot. At your orders.',
   'Optimus. I assist with flying and watch the systems.',
   'Copilot Optimus, {pilote}. Operational.']},
 'dialogue.wellbeing': {'any': [
   'All my circuits are nominal, {pilote}.', 'Operational. And you?',
   'No anomaly on my side.']},

 'system.confirm': {'any': ['Confirmed.', 'Executing.']},
 'system.deny': {'any': ['Cancelled.', 'Copy that, I will do nothing.']},

 'system.propose': {'clarify': [
   'Do you mean {command}?', '{command}, is that right?',
   'I think I heard {command}. Shall I confirm?']},

 'dialogue.acknowledge': {'any': [
   'At your service, {pilote}.', 'It is my job.',
   'Do not thank me, thank engineering.']},
 'dialogue.reaction': {'any': [
   'I would rather not have seen that, {pilote}.', 'Situation noted.',
   'I would rather not comment.']},
 'system.startup': {'any': [
   'Optimus online. At your orders, {pilote}.',
   'Systems initialised. I am listening.']},
}

LEXICON = collections.OrderedDict([
    ('address_user', ['commander', 'captain']),
    ('forbidden_phrases', [
        'as a language model',
        'i am an artificial intelligence',
        'i cannot help you with that',
        'lol',
        'lmao',
    ]),
    ('replacements', collections.OrderedDict([
        ('ok', 'copy'),
        ('alright', 'affirmative'),
    ])),
])


def main():
    source = 'data/copilots/optimus/responses.fr.json'
    target = 'data/copilots/optimus/responses.en.json'

    d = json.load(io.open(source, encoding='utf-8'),
                  object_pairs_hook=collections.OrderedDict)

    d['locale'] = 'en-US'
    d['notes'] = [
        "Engendre depuis responses.fr.json : memes entrees, memes evenements, memes",
        "« requires ». Seul le texte change. {pilote} et {command} restent tels quels,",
        "le composeur les cherchant litteralement.",
        "Le lexique vit ICI et non dans personality.json : les formes d'adresse sont de la",
        "langue, tandis que les curseurs de caractere n'en sont pas. Sans cela un copilote",
        "anglais aurait dit « At your orders, commandant ».",
        "DECOR (D32) : les repliques de system.status enoncent des releves qu'Optimus ne",
        "mesure pas. Memes reserves que dans la version francaise.",
    ]

    d['lexicon'] = LEXICON

    for key, events in d['entries'].items():
        assert key in EN, 'entree non traduite : ' + key
        for event, variants in events.items():
            assert event in EN[key], 'evenement non traduit : %s / %s' % (key, event)
            texts = EN[key][event]
            assert len(texts) == len(variants), (
                '%s / %s : %d variantes contre %d' % (key, event, len(texts), len(variants)))
            for variant, text in zip(variants, texts):
                variant['text'] = text

    extra = [k for k in EN if k not in d['entries']]
    assert not extra, 'entrees en trop : ' + ', '.join(extra)

    io.open(target, 'w', encoding='utf-8', newline='\n').write(
        json.dumps(d, ensure_ascii=False, indent=2) + '\n')

    total = sum(len(v) for e in d['entries'].values() for v in e.values())
    print(len(d['entries']), 'entrees,', total, 'variantes')


main()
