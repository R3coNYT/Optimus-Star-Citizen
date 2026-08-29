/*
 * Le panneau de reglages d'une touche Optimus.
 *
 * Deux portees, et les melanger serait penible a l'usage :
 *
 *   - le port et le jeton sont GLOBAUX. Les saisir une fois doit suffire pour
 *     toutes les touches, sinon un pupitre de six boutons demande six collages
 *     du meme secret.
 *
 *   - la commande et le texte appartiennent a LA touche. Deux boutons « executer »
 *     n'ont aucune raison de lancer la meme chose.
 *
 * Le panneau essaie la connexion des qu'il a de quoi le faire, et le dit. C'est
 * le seul endroit ou le pilote peut apprendre que son jeton est perime : sur le
 * boitier, une touche qui echoue clignote sans rien expliquer.
 */

'use strict';

let deck = null;
let uuid = null;
let action = null;
let context = null;

let global = { port: 8731, token: '' };
let own = {};

const el = (id) => document.getElementById(id);

/* eslint-disable-next-line no-unused-vars */
function connectElgatoStreamDeckSocket(port, inUUID, registerEvent, info, actionInfo) {
  uuid = inUUID;

  const parsed = JSON.parse(actionInfo);
  action = parsed.action;
  context = parsed.context;
  own = parsed.payload.settings || {};

  el('command-block').classList.toggle('hidden', action !== 'com.optimus.copilot.command');
  el('say-block').classList.toggle('hidden', action !== 'com.optimus.copilot.say');

  el('polarity').value = own.polarity || '';
  el('text').value = own.text || '';

  deck = new WebSocket('ws://127.0.0.1:' + port);

  deck.onopen = () => {
    send({ event: registerEvent, uuid: uuid });
    send({ event: 'getGlobalSettings', context: uuid });
  };

  deck.onmessage = (message) => {
    const data = JSON.parse(message.data);

    if (data.event === 'didReceiveGlobalSettings') {
      global = Object.assign({ port: 8731, token: '' }, data.payload.settings);

      el('port').value = global.port;
      el('token').value = global.token;

      probe();
    }
  };

  wire();
}

function send(payload) {
  if (deck && deck.readyState === WebSocket.OPEN) {
    deck.send(JSON.stringify(payload));
  }
}

function saveGlobal() {
  send({ event: 'setGlobalSettings', context: uuid, payload: global });
}

function saveOwn() {
  send({ event: 'setSettings', context: uuid, payload: own });
}

function wire() {
  el('port').addEventListener('change', () => {
    global.port = Number(el('port').value) || 8731;
    saveGlobal();
    probe();
  });

  // « input » et non « change » : un jeton se colle, et attendre la perte du
  // focus pour le prendre en compte donne l'impression que rien ne s'est passe.
  el('token').addEventListener('input', () => {
    global.token = el('token').value.trim();
    saveGlobal();
    probe();
  });

  el('command').addEventListener('change', () => {
    own.command = el('command').value;
    saveOwn();
  });

  el('polarity').addEventListener('change', () => {
    own.polarity = el('polarity').value;
    saveOwn();
  });

  el('text').addEventListener('input', () => {
    own.text = el('text').value;
    saveOwn();
  });
}

function say(message, kind) {
  const box = el('status');
  box.textContent = message;
  box.className = 'status' + (kind ? ' ' + kind : '');
}

async function probe() {
  if (!global.token) {
    say('Paste the token from Optimus, under Settings ▸ Local API.');
    return;
  }

  say('Checking…');

  try {
    const status = await call('/api/status');

    say(
      'Connected · copilot ' + status.copilot +
      ' · ' + status.commands + ' commands' +
      ' · microphone ' + (status.listening ? 'open' : 'closed'),
      'ok');

    if (action === 'com.optimus.copilot.command') {
      await fillCommands();
    }
  } catch (error) {
    // Le message porte la cause : « 401 » veut dire jeton, l'echec reseau veut
    // dire Optimus ferme ou API eteinte. Les confondre coute une demi-heure.
    if (String(error.message) === 'HTTP 401') {
      say('Optimus answers, but refuses this token. Regenerate it in Settings.', 'bad');
    } else {
      say('No answer on port ' + global.port +
          '. Is Optimus running, with the local API switched on?', 'bad');
    }
  }
}

async function fillCommands() {
  const listing = await call('/api/commands');
  const select = el('command');

  select.innerHTML = '';

  listing.commands
    .slice()
    .sort((a, b) => (a.category + a.name).localeCompare(b.category + b.name))
    .forEach((command) => {
      const option = document.createElement('option');
      option.value = command.id;
      option.textContent = command.category + ' · ' + command.name;
      select.appendChild(option);
    });

  // La commande enregistree peut ne plus exister : le pilote a change de langue,
  // ou supprime sa macro. On la garde visible plutot que de la remplacer en
  // douce par la premiere de la liste.
  if (own.command && !listing.commands.some((c) => c.id === own.command)) {
    const orphan = document.createElement('option');
    orphan.value = own.command;
    orphan.textContent = own.command + '  (not in the catalogue)';
    select.insertBefore(orphan, select.firstChild);
  }

  if (own.command) {
    select.value = own.command;
  } else {
    own.command = select.value;
    saveOwn();
  }
}

async function call(path) {
  const response = await fetch('http://127.0.0.1:' + global.port + path, {
    headers: { Authorization: 'Bearer ' + global.token },
  });

  if (!response.ok) {
    throw new Error('HTTP ' + response.status);
  }

  return response.json();
}
