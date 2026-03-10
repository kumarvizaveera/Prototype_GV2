# Recommended Mint Count and Improvements

## Recommended Mint Count

Use the same supply pattern for all 8 characters so testing stays simple and balanced.

### Per character
- **Common:** 12
- **Rare:** 5
- **Epic:** 2
- **Legendary:** 1

**Total per character:** 20  
**Total for 8 characters:** 160 NFTs

This is enough to test:
- minting flow
- rarity display
- inventory spread
- matchmaking feel
- upgrade perception

---

## Recommended Rarity Multiplier

Apply rarity as a final layer on top of each character’s own base stat profile.

- **Common:** 1.00x
- **Rare:** 1.06x
- **Epic:** 1.12x
- **Legendary:** 1.18x

### Formula
**Final Stat = Character Base Multiplier × Rarity Multiplier**

Example:  
If Projectile Damage = **1.10** and rarity = **Rare 1.06**  
Final = **1.166**

---

# Character Improvements

## 1) Aaryaveer — Balanced striker
Good all-rounder, beginner-friendly.

- Speed: **1.03**
- Steering: **1.03**
- Boost: **1.02**
- Projectile Damage: **1.08**
- Projectile Range: **1.05**
- Projectile Speed: **1.05**
- Projectile Fire Rate: **1.03**
- Projectile Reload: **1.02**
- Missile Damage: **1.05**
- Missile Range: **1.03**
- Missile Speed: **1.03**
- Missile Fire Rate: **1.02**
- Missile Reload: **1.02**

## 2) Ishvaya — Precision / control
More elegant, accurate, tactical.

- Speed: **1.02**
- Steering: **1.08**
- Boost: **1.01**
- Projectile Damage: **1.05**
- Projectile Range: **1.10**
- Projectile Speed: **1.08**
- Projectile Fire Rate: **1.02**
- Projectile Reload: **1.02**
- Missile Damage: **1.03**
- Missile Range: **1.08**
- Missile Speed: **1.06**
- Missile Fire Rate: **1.02**
- Missile Reload: **1.03**

## 3) Vyanika — Mobility / evasive
Fast and slippery.

- Speed: **1.08**
- Steering: **1.10**
- Boost: **1.08**
- Projectile Damage: **1.00**
- Projectile Range: **1.02**
- Projectile Speed: **1.05**
- Projectile Fire Rate: **1.04**
- Projectile Reload: **1.03**
- Missile Damage: **0.98**
- Missile Range: **1.00**
- Missile Speed: **1.03**
- Missile Fire Rate: **1.03**
- Missile Reload: **1.02**

## 4) Rudraansh — Heavy burst
Aggressive, high kill pressure.

- Speed: **0.98**
- Steering: **0.99**
- Boost: **1.00**
- Projectile Damage: **1.15**
- Projectile Range: **1.03**
- Projectile Speed: **1.02**
- Projectile Fire Rate: **1.00**
- Projectile Reload: **0.98**
- Missile Damage: **1.12**
- Missile Range: **1.04**
- Missile Speed: **1.02**
- Missile Fire Rate: **1.00**
- Missile Reload: **0.98**

## 5) Zorvan — Missile hunter
Modern, tactical, lock-on threat.

- Speed: **1.02**
- Steering: **1.01**
- Boost: **1.03**
- Projectile Damage: **1.02**
- Projectile Range: **1.03**
- Projectile Speed: **1.04**
- Projectile Fire Rate: **1.02**
- Projectile Reload: **1.02**
- Missile Damage: **1.12**
- Missile Range: **1.10**
- Missile Speed: **1.10**
- Missile Fire Rate: **1.04**
- Missile Reload: **1.04**

## 6) Kaevik — Rapid assault
High pressure shooter.

- Speed: **1.05**
- Steering: **1.04**
- Boost: **1.04**
- Projectile Damage: **1.06**
- Projectile Range: **1.02**
- Projectile Speed: **1.06**
- Projectile Fire Rate: **1.10**
- Projectile Reload: **1.08**
- Missile Damage: **1.00**
- Missile Range: **1.00**
- Missile Speed: **1.02**
- Missile Fire Rate: **1.03**
- Missile Reload: **1.03**

## 7) Nysera — Long-range specialist
Clean, advanced, elite sniper feel.

- Speed: **1.01**
- Steering: **1.04**
- Boost: **1.02**
- Projectile Damage: **1.07**
- Projectile Range: **1.12**
- Projectile Speed: **1.10**
- Projectile Fire Rate: **1.00**
- Projectile Reload: **1.01**
- Missile Damage: **1.04**
- Missile Range: **1.10**
- Missile Speed: **1.08**
- Missile Fire Rate: **1.00**
- Missile Reload: **1.01**

## 8) Virexa — Glass cannon
Dangerous, stylish, high-risk high-reward.

- Speed: **1.04**
- Steering: **1.03**
- Boost: **1.05**
- Projectile Damage: **1.12**
- Projectile Range: **1.04**
- Projectile Speed: **1.07**
- Projectile Fire Rate: **1.06**
- Projectile Reload: **1.02**
- Missile Damage: **1.08**
- Missile Range: **1.03**
- Missile Speed: **1.05**
- Missile Fire Rate: **1.04**
- Missile Reload: **1.01**

---

## Best Structure for Testnet

- Same rarity counts for every character
- Different base stat identity per character
- Mild rarity scaling

---

## Safe Balance Notes

For Battle Royale fairness:
- Rarity should not multiply everything too aggressively
- Better to boost 2–4 signature stats per character over time
- Keep movement boosts lower than weapon boosts
- Avoid letting speed + steering + boost all stack too high together

### Safe caps
- **Movement stats:** around **1.10–1.12**
- **Weapon stats:** around **1.15–1.20**
- After rarity, try not to exceed **1.22–1.25** too often

### Normalization guide
- **1.00** = baseline
- **1.03–1.08** = light bonus
- **1.09–1.14** = strong bonus
- **1.15+** = signature strength
