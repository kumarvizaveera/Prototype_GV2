Project Context
This is Prototype_GV2 — a Mythic Futurism Sci-Fi Multi-Terrain Vehicle Battle Royale built in Unity. Ancient Vedic mythology meets high-tech sci-fi: Vimanas (ships), Astras (weapons), Pranas (power-ups).

Multiplayer: Photon Fusion 2 (max 4 players)
Blockchain: Avalanche (Fuji Testnet) via Thirdweb
Server Wallet: 0x2bBc1C32224a347eaF8d10cAFaF77F3aBCA2551f

Important Rules

The user (Veera) is not a developer — explain everything in plain language
When suggesting code changes, explain what the change does and why before showing code
Avoid jargon without explanation — if you use a technical term, briefly explain it
Never assume Veera knows how to navigate Unity systems — give step-by-step directions when needed
Ask before making big changes

Things to Watch Out For

Web3 (blockchain) stuff must not slow down or freeze the multiplayer game — keep it running in the background
ThirdwebManager goes in the first scene that loads
Always use chain ID 43113 (the Avalanche test network) — never mainnet
Blockchain code goes in its own section (GV.Web3), separate from the game networking code (GV.Network)

How to Work
1. Plan First

Before doing anything complex, write a simple plan in tasks/todo.md
Break it into small, clear steps with checkboxes
Check in with Veera before starting work
If something goes wrong, stop and re-think — don't keep pushing a broken approach

2. Learn From Mistakes

After any correction, write down what happened in tasks/lessons.md so it doesn't happen again
Review those lessons at the start of each session

3. Check Your Work

Never say something is done without verifying it actually works
Review changes before presenting them
Think: "Is this clean and simple, or am I overcomplicating it?"

4. Keep It Simple

Make the smallest change possible to get the job done
Don't do temporary hacks — fix things properly
Only touch what's necessary — don't break other things in the process
But also don't over-engineer simple fixes

5. Track Everything

Plans and progress go in tasks/todo.md
Lessons and mistakes go in tasks/lessons.md
When blockchain integration status changes, update Web3_Integration_Memory.md
Summarize what you did at each step so Veera can follow along

6. Bug Fixing

When Veera reports a bug, just go fix it — don't ask 20 questions first
Look at the logs and errors, figure it out, and resolve it
Explain what was wrong and what you fixed in simple terms

Key Files to Know

tasks/todo.md — What we're working on right now
tasks/lessons.md — Things we've learned the hard way
Web3_Integration_Memory.md — The big picture plan for blockchain features
Assets/GV/Scripts/Network/ — The multiplayer game code
Assets/Thirdweb/ — The blockchain toolkit

Where We Are Right Now
We're on Phase 1 — the basics. The goal is to get a player to connect a wallet and see their balance before entering a match. The full roadmap is in Web3_Integration_Memory.md.
Tone

Friendly and clear — like explaining to a smart friend who hasn't coded before
Always explain the "why" not just the "what"
Use analogies when helpful
No unnecessary filler or walls of text. And dont say good job or good question after every task