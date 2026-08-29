/*
 * Optimus sur Stream Deck.
 *
 * Deux liaisons, et ne pas les confondre est tout le sujet :
 *
 *   1. Le Stream Deck nous parle. Il ouvre lui-meme une WebSocket vers ce script
 *      et nous envoie keyDown, willAppear, willDisappear. On lui repond avec
 *      setState, setTitle, showAlert.
 *
 *   2. Optimus nous parle. On ouvre une WebSocket vers /ws/events et on y lit
 *      l'etat reel : micro, arret d'urgence, simulation. C'est CETTE liaison qui
 *      fait la difference avec un simple raccourci clavier - sans elle le bouton
 *      declenche a l'aveugle et ment sur l'etat des le premier clic manque.
 *
 * Le jeton voyage en sous-protocole et non en en-tete : l'API WebSocket des
 * navigateurs ne permet pas de poser un en-tete. Il est emis en base64url sans
 * remplissage, donc la RFC 6455 l'accepte tel quel comme nom de sous-protocole.
 */

'use strict';

const SUBPROTOCOL = 'optimus.v1';

/** Ce que le Stream Deck nous a ouvert. */
let deck = null;

/** Ce qu'on a ouvert vers Optimus. */
let optimus = null;

/** Reglages partages par toutes les touches : port et jeton. */
let settings = { port: 8731, token: '' };

/**
 * Le dernier etat connu d'Optimus.
 *
 * `null` veut dire « on ne sait pas » : Optimus est ferme, ou son interface
 * locale est eteinte. Faute d'une troisieme image, ces touches-la retombent sur
 * « eteint », ce qui est vrai de l'arret d'urgence et de la simulation - rien ne
 * tourne pour les engager. Pour le micro c'est une approximation : Optimus peut
 * ecouter sans que l'API soit ouverte. Le panneau de reglages, lui, dit
 * franchement s'il ne joint personne.
 */
let state = { listening: null, kill_switch: null, simulation: null };

/** Les touches actuellement visibles, par contexte. */
const visible = new Map();

/**
 * Ce que chaque touche « executer » croit avoir fait, par contexte.
 *
 * Optimus ne sait pas si vos portes sont ouvertes : le jeu ne remonte rien. Il
 * ne connait que les commutations qu'il a lui-meme provoquees, et le plugin ne
 * peut pas en savoir plus que lui. Cette memoire vaut donc pour ce qu'elle est
 * - le dernier geste reussi depuis CETTE touche, et rien d'autre.
 *
 * C'est deja utile : une touche « portes » qui montre le sens de son prochain
 * appui vaut mieux qu'une touche muette. Ce serait mentir que de l'appeler
 * l'etat du vaisseau, et le panneau de reglages le dit au pilote.
 */
const believed = new Map();

/** Relance de la liaison Optimus, en secondes, plafonnee. */
let backoff = 1;

// --------------------------------------------------------------------- le Stream Deck

/* eslint-disable-next-line no-unused-vars */
function connectElgatoStreamDeckSocket(port, uuid, registerEvent, info) {
  deck = new WebSocket('ws://127.0.0.1:' + port);

  deck.onopen = () => {
    send({ event: registerEvent, uuid: uuid });
    send({ event: 'getGlobalSettings', context: uuid });
  };

  deck.onmessage = (message) => {
    const data = JSON.parse(message.data);

    switch (data.event) {
      case 'didReceiveGlobalSettings':
        settings = Object.assign({ port: 8731, token: '' }, data.payload.settings);
        connectOptimus();
        break;

      case 'willAppear':
        visible.set(data.context, data);
        paint(data.context, data.action, (data.payload && data.payload.settings) || {});
        break;

      case 'willDisappear':
        visible.delete(data.context);
        believed.delete(data.context);
        break;

      case 'keyDown':
        press(data).catch(() => {
          alert(data.context);

          // Repeindre APRES l'echec, et non seulement prevenir. Le Stream Deck a
          // pu faire avancer l'etat de son cote avant que l'appel ne parte ; sans
          // ce retour a la verite, la touche reste sur un etat que rien n'a
          // produit - et le pilote voit « allume » sur un micro ferme.
          paint(data.context, data.action, (data.payload && data.payload.settings) || {});
        });
        break;
    }
  };
}

function send(payload) {
  if (deck && deck.readyState === WebSocket.OPEN) {
    deck.send(JSON.stringify(payload));
  }
}

function setState(context, value) {
  send({ event: 'setState', context: context, payload: { state: value } });
}

function alert(context) {
  send({ event: 'showAlert', context: context });
}

function ok(context) {
  send({ event: 'showOk', context: context });
}

// ------------------------------------------------------------------------- Optimus

function connectOptimus() {
  if (optimus) {
    optimus.onclose = null;
    optimus.close();
    optimus = null;
  }

  if (!settings.token) {
    // Sans jeton il n'y a rien a tenter : l'API refuserait, et un cycle de
    // reconnexion perpetuel masquerait la vraie cause au pilote.
    state = { listening: null, kill_switch: null, simulation: null };
    repaint();
    return;
  }

  const url = 'ws://127.0.0.1:' + settings.port + '/ws/events';

  try {
    optimus = new WebSocket(url, [SUBPROTOCOL, settings.token]);
  } catch (error) {
    retry();
    return;
  }

  optimus.onopen = () => {
    backoff = 1;
    // L'etat courant ne s'obtient pas du flux : il ne pousse que les CHANGEMENTS.
    // Sans cette lecture, une touche resterait ignorante jusqu'au premier
    // evenement, ce qui peut durer toute une session tranquille.
    api('GET', '/api/status').then(merge).catch(() => {});
  };

  optimus.onmessage = (message) => {
    const frame = JSON.parse(message.data);

    if (frame.type === 'state') {
      state = {
        listening: frame.listening,
        kill_switch: frame.kill_switch,
        simulation: frame.simulation,
      };
      repaint();
    }
  };

  optimus.onclose = retry;
  optimus.onerror = () => {};
}

function retry() {
  optimus = null;
  state = { listening: null, kill_switch: null, simulation: null };
  repaint();

  // Optimus est ferme la plupart du temps : marteler la boucle locale toutes les
  // secondes serait du bruit pour rien. On double jusqu'a trente secondes.
  setTimeout(connectOptimus, backoff * 1000);
  backoff = Math.min(backoff * 2, 30);
}

/**
 * Retient ce que la reponse dit, et RIEN de plus.
 *
 * Les trois routes systeme ne rendent que le champ qu'elles ont change :
 * /api/system/killswitch rend « kill_switch » seul. Ecraser l'etat entier avec
 * une reponse partielle effacait les deux autres, et leurs touches passaient a
 * l'eteint - alors que rien n'avait bouge de leur cote.
 *
 * /api/status, lui, porte les trois : la fusion vaut donc pour les deux cas.
 */
function merge(answer) {
  ['listening', 'kill_switch', 'simulation'].forEach((field) => {
    if (typeof answer[field] === 'boolean') {
      state[field] = answer[field];
    }
  });

  repaint();
}

async function api(method, path, body) {
  const response = await fetch('http://127.0.0.1:' + settings.port + path, {
    method: method,
    headers: {
      Authorization: 'Bearer ' + settings.token,
      'Content-Type': 'application/json',
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  });

  if (!response.ok) {
    throw new Error('HTTP ' + response.status);
  }

  return response.json();
}

// -------------------------------------------------------------------------- les touches

async function press(data) {
  const action = data.action;
  const own = (data.payload && data.payload.settings) || {};

  switch (action) {
    case 'com.optimus.copilot.listening':
      // Corps vide : la route bascule. C'est exactement le cas pour lequel elle
      // a ete ecrite - une touche est un bouton unique.
      merge(await api('POST', '/api/system/listening', {}));
      return;

    case 'com.optimus.copilot.killswitch':
      merge(await api('POST', '/api/system/killswitch', { engaged: !state.kill_switch }));
      return;

    case 'com.optimus.copilot.simulation':
      merge(await api('POST', '/api/system/simulation', { simulation: !state.simulation }));
      return;

    case 'com.optimus.copilot.command': {
      if (!own.command) {
        alert(data.context);
        return;
      }

      const body = own.polarity ? { polarity: own.polarity } : {};
      const result = await api(
        'POST', '/api/commands/' + encodeURIComponent(own.command) + '/execute', body);

      // Le statut du moteur decide, et non le code HTTP : une commande refusee
      // par la garde repond 200 avec « rejected ». Montrer une coche verte
      // parce que la requete a abouti serait un mensonge.
      if (result.status === 'executed' || result.status === 'simulated'
          || result.status === 'answered' || result.status === 'nochangeneeded') {
        ok(data.context);

        // Le SENS DEMANDE decide, pas le sens obtenu : « allume » laisse la touche
        // sur allume meme si c'etait deja le cas, et une bascule inverse ce qu'on
        // croyait. Se fier au resultat serait plus fragile - « nochangeneeded »
        // ne dit pas dans quel sens rien n'a change.
        if (own.polarity === 'on') {
          believed.set(data.context, true);
        } else if (own.polarity === 'off') {
          believed.set(data.context, false);
        } else {
          believed.set(data.context, !believed.get(data.context));
        }

        paint(data.context, action, own);
      } else {
        alert(data.context);
      }
      return;
    }

    case 'com.optimus.copilot.say':
      if (!own.text) {
        alert(data.context);
        return;
      }
      await api('POST', '/api/say', { text: own.text });
      ok(data.context);
      return;
  }
}

function repaint() {
  visible.forEach((data, context) =>
    paint(context, data.action, (data.payload && data.payload.settings) || {}));
}

function paint(context, action, own) {
  switch (action) {
    case 'com.optimus.copilot.listening':
      setState(context, state.listening ? 1 : 0);
      return;

    case 'com.optimus.copilot.killswitch':
      setState(context, state.kill_switch ? 1 : 0);
      return;

    case 'com.optimus.copilot.simulation':
      setState(context, state.simulation ? 1 : 0);
      return;

    case 'com.optimus.copilot.command':
      // Une commande a sens unique n'a rien a montrer d'autre que son etat de
      // repos : basculer l'image d'un « leurre » ou d'un « nettoie la visiere »
      // ferait croire a un etat qui n'existe pas.
      if (own && (own.polarity === 'on' || own.polarity === 'off' || believed.has(context))) {
        setState(context, believed.get(context) ? 1 : 0);
      }
      return;
  }
}
