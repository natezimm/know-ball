import { leagues, preferenceKey, readPreferences, dailySet, practiceSet, score, topics, eras, easternDate, completionStreak } from './model.mjs';

const stage = document.querySelector('#stage');
const escape = text => String(text).replace(/[&<>"']/g, char => ({ '&':'&amp;', '<':'&lt;', '>':'&gt;', '"':'&quot;', "'":'&#39;' })[char]);
let storage;
try { storage = window.localStorage; } catch { /* Preferences still work for this visit. */ }
let selected = readPreferences(storage);
const progressKey = 'knowball.numbers.preview.progress.v2';
let progress;
try { progress = JSON.parse(storage?.getItem(progressKey)); } catch {}
if (!progress || !Array.isArray(progress.seen) || !progress.daily || typeof progress.daily !== 'object') progress = {seen:[], daily:{}, best:0};
let chosenTopics = topics.slice(), chosenEras = eras.slice(), roundDate = easternDate();
const save = () => { try { storage?.setItem(progressKey, JSON.stringify(progress)); } catch {} };
const practice = () => practiceSet(questions, selected, Math.random, {topics:chosenTopics, eras:chosenEras, seen:progress.seen});
const stats = () => `Daily completion streak: ${completionStreak(Object.keys(progress.daily), easternDate())} · Practice best: ${progress.best || 0}/5 · Saved on this browser`;

let questions, mode = 'daily', round = [], answers = [], index = 0, pending = false;
const number = value => value.toLocaleString('en-US');
const focusHeading = () => stage.querySelector('h2')?.focus();
const card = html => { stage.innerHTML = `<div class="card">${html}</div>`; };
const heading = text => `<h2 tabindex="-1">${escape(text)}</h2>`;

function switchMode(next) {
  if (round.length && answers.length && index < 5 && !confirm('Leave this unfinished preview round? Its answers will be reset.')) return;
  mode = next; round = []; answers = []; index = 0; pending = false;
  for (const name of ['daily', 'practice']) document.getElementById(name).setAttribute('aria-pressed', String(mode === name));
  setup(); focusHeading();
}

function setup() {
  if (mode === 'daily') {
    card(`<p class="meta">Shared daily · ${easternDate()}</p>${heading('Five questions. A fresh challenge daily.')}<p>Pick one of four answers, then lock it in. Discover the story behind every answer, even when you miss.</p><p>Awards, teams, seasons and stats. No timer. Resets at midnight Eastern.</p><p>${stats()}</p><button class="primary" id="start">Play daily preview</button>`);
    stage.querySelector('#start').onclick = () => {
      roundDate = easternDate();
      const saved = progress.daily[roundDate];
      if (saved?.length === 5 && saved.every(a => questions.some(q => q.id === a.id))) {
        answers = saved.map(a => ({question:questions.find(q => q.id === a.id), optionId:a.optionId}));
        round = answers.map(a => a.question); index = 5; renderResult(); focusHeading();
      } else start(dailySet(questions, roundDate));
    };
    if (progress.daily[easternDate()]) stage.querySelector('#start').textContent = 'View today’s result';
    return;
  }
  card(`<p class="meta">Your leagues. Your practice.</p>${heading('Choose your leagues')}<p>Practice only changes this round. Everyone still gets the same daily quiz.</p><form id="setup"><fieldset><legend>Select one or more leagues</legend><button type="button" id="all">All leagues</button><div class="leagues">${leagues.map(l => `<label class="choice"><input type="checkbox" name="league" value="${l}" ${selected.includes(l) ? 'checked' : ''}>${l.toUpperCase()}</label>`).join('')}</div></fieldset><details><summary>Customize topics and eras</summary>${[['topic', topics, chosenTopics], ['era', eras, chosenEras]].map(([name, values, picked]) => `<fieldset><legend>${name === 'topic' ? 'Topics' : 'Eras (season start year)'}</legend>${values.map(v => `<label class="choice"><input type="checkbox" name="${name}" value="${v}" ${picked.includes(v) ? 'checked' : ''}>${v}</label>`).join('')}</fieldset>`).join('')}</details><p id="selection" aria-live="polite"></p><p class="error" id="error" role="alert"></p><button class="primary">Start practice</button></form>`);
  const sync = () => {
    selected = [...stage.querySelectorAll('input[name="league"]:checked')].map(input => input.value);
    stage.querySelector('#selection').textContent = selected.length ? `Five questions · ${selected.map(l => l.toUpperCase()).join(' + ')}` : 'No leagues selected.';
    chosenTopics = [...stage.querySelectorAll('input[name="topic"]:checked')].map(i => i.value);
    chosenEras = [...stage.querySelectorAll('input[name="era"]:checked')].map(i => i.value);
    stage.querySelector('#error').textContent = '';
    try { storage?.setItem(preferenceKey, JSON.stringify(selected)); } catch { /* Best effort, never blocks play. */ }
  };
  stage.querySelectorAll('input').forEach(input => input.onchange = sync);
  stage.querySelector('#all').onclick = () => { stage.querySelectorAll('input[name="league"]').forEach(input => input.checked = true); sync(); };
  stage.querySelector('#setup').onsubmit = event => {
    event.preventDefault();
    try {
      if (!chosenTopics.length || !chosenEras.length) throw new Error('Select at least one topic and era.');
      start(practice());
    }
    catch (error) { stage.querySelector('#error').textContent = error.message; }
  };
  sync();
}

function start(set) { round = set.map(q => ({...q, options:[...q.options]}));
  for (const q of round) for (let i = q.options.length - 1; i > 0; i--) { const j = Math.floor(Math.random() * (i+1)); [q.options[i],q.options[j]] = [q.options[j],q.options[i]]; }
 answers = []; index = 0; pending = false; renderQuestion(); focusHeading(); }

function evidence(q) {
  return `<p>${escape(q.explanation)}</p><ul class="values">${q.options.filter(o => Number.isFinite(o.value)).map(o => `<li><span>${escape(o.label)}</span><strong>${number(o.value)}</strong></li>`).join('')}</ul><p class="source">${escape(q.unit || q.topic)} · ${q.scope === 'postseason' ? 'Postseason' : 'Regular season'}.</p><p class="source">Source: <a href="${escape(q.source.url)}" target="_blank" rel="noopener noreferrer">${escape(q.source.credit)}</a>${q.source.licenseUrl ? ` · <a href="${q.source.licenseUrl}" target="_blank" rel="noopener noreferrer">CC BY 4.0</a> · Weekly totals aggregated by Know Ball; no endorsement.` : ''}</p>`;
}

function renderQuestion() {
  const q = round[index], answer = answers[index];
  const correct = q.options.find(o => o.id === q.correctOptionId);
  card(`<div class="meta"><span>${mode === 'daily' ? 'Daily preview' : 'Practice'} · ${q.league.toUpperCase()}</span><span>${q.topic === 'seasons' && !answer ? 'Name the season' : escape(q.season)} · ${escape(q.topic || 'stats')}</span></div><div class="progress" aria-label="${answers.length} of 5 answered">${Array.from({length:5}, (_,i) => `<span class="${i < answers.length ? 'done' : ''}"></span>`).join('')}</div><p class="meta">Question ${index + 1} of 5 · Score ${score(answers)}/${answers.length}</p>${heading(q.prompt)}<form id="answer"><fieldset ${answer ? 'disabled' : ''}><legend>Choose one answer</legend><div class="choices">${q.options.map(o => `<label class="choice ${answer && o.id === q.correctOptionId ? 'correct' : ''}"><input type="radio" name="answer" value="${o.id}" ${answer?.optionId === o.id ? 'checked' : ''}><span>${escape(o.label)}</span>${answer && o.id === q.correctOptionId ? '<span class="tag">Correct</span>' : ''}</label>`).join('')}</div></fieldset>${answer ? '' : '<button class="primary" id="lock" disabled>Lock in answer</button>'}</form>${answer ? `<section class="feedback ${answer.optionId !== q.correctOptionId ? 'missed' : ''}" aria-label="Answer explanation"><h3 tabindex="-1" id="feedback">${answer.optionId === q.correctOptionId ? 'Correct!' : 'Not quite.'} ${escape(correct.label)}.</h3>${answers.length > 1 && answers.slice(-2).every(a => a.optionId === a.question.correctOptionId) ? '<p role="status">You’re on a roll — consecutive correct answers!</p>' : ''}${evidence(q)}<button class="primary" id="next">${index === 4 ? 'See results' : 'Next question'}</button></section>` : ''}`);
  if (answer) {
    stage.querySelector('#next').onclick = () => { index++; pending = false; if (index === 5) renderResult(); else renderQuestion(); focusHeading(); };
    return;
  }
  stage.querySelectorAll('input').forEach(input => input.onchange = () => stage.querySelector('#lock').disabled = false);
  stage.querySelector('#answer').onsubmit = event => {
    event.preventDefault();
    const optionId = new FormData(event.currentTarget).get('answer');
    if (pending || !q.options.some(o => o.id === optionId)) return;
    pending = true; answers.push({ question:q, optionId });
    if (mode === 'practice') { progress.seen = [...progress.seen.filter(id => id !== q.id),q.id].slice(-100); save(); } renderQuestion(); stage.querySelector('#feedback').focus();
  };
}

function renderResult() {
  if (mode === 'daily' && !progress.daily[roundDate]) progress.daily[roundDate] = answers.map(a => ({id:a.question.id,optionId:a.optionId}));
  if (mode === 'practice') progress.best = Math.max(progress.best || 0, score(answers));
  save();
  card(`<p class="meta">${mode === 'daily' ? 'Daily preview' : `Practice · ${selected.map(l => l.toUpperCase()).join(' + ')}`} completed</p>${heading('That’s the final score.')}<p class="score">${score(answers)} / 5</p><p>${answers.map((a,i) => `${i + 1}: ${a.optionId === a.question.correctOptionId ? 'Correct' : 'Missed'}`).join(' · ')}</p><p>${stats()}</p><p>Every answer has a story. Review yours below.</p>${answers.map((a,i) => `<details><summary>Question ${i + 1} · ${a.question.league.toUpperCase()} · ${a.optionId === a.question.correctOptionId ? 'Correct' : 'Missed'}</summary><p>${escape(a.question.prompt)}</p><p>Your answer: ${escape(a.question.options.find(o => o.id === a.optionId).label)}</p>${evidence(a.question)}</details>`).join('')}<button id="replay" class="primary">Play another round</button><button id="again">Customize practice</button><button id="share">Share result</button><p id="share-status" role="status"></p>`);
  stage.querySelector('#again').onclick = () => switchMode('practice');
  stage.querySelector('#replay').onclick = () => {
    mode = 'practice';
    document.querySelector('#daily').setAttribute('aria-pressed','false'); document.querySelector('#practice').setAttribute('aria-pressed','true');
    try { start(practice()); } catch { setup(); }
  };
  stage.querySelector('#share').onclick = async () => {
    const text = `Numbers Don't Lie · ${mode === 'daily' ? roundDate : 'Practice'}\n${score(answers)}/5 ${answers.map(a => a.optionId === a.question.correctOptionId ? '🟩' : '⬜').join('')}\nhttps://knowball.nathanzimmerman.com/preview/numbers-dont-lie/index.html?ngsw-bypass=true`;
    try { await navigator.clipboard.writeText(text); stage.querySelector('#share-status').textContent = 'Copied!'; }
    catch { stage.querySelector('#share-status').textContent = text; }
  };
}

try {
  const response = await fetch('./questions.json');
  if (!response.ok) throw new Error('Unable to load the sample questions.');
  questions = (await response.json()).questions;
  document.querySelector('#daily').onclick = () => switchMode('daily');
  document.querySelector('#practice').onclick = () => switchMode('practice');
  setup();
} catch {
  card(`${heading('The preview couldn’t load.')}<p>Serve the repository over HTTP using the instructions in the prototype README, then reload.</p><button id="retry">Reload preview</button>`);
  stage.querySelector('#retry').onclick = () => location.reload();
}
