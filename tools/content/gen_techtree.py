#!/usr/bin/env python3
"""
Generates SimWorld's authored tech tree: one ResearchProjectDef XML file per era, written to
src/SimWorld.Core/Data/Core/Defs/ResearchProjectDefs/Research_<order>_<EraDefName>.xml.

The tree itself lives below as plain data (see PROJECTS_BY_ERA) -- to edit the tree, edit that
table and re-run this script (see tools/content/README.md). Everything else in this file is
plumbing: assigning tech levels and view coordinates from era/track, picking a cost inside each
era's cost band from a project's depth in the prerequisite DAG, validating the result, and
writing the XML.

Python 3, standard library only.
"""
from __future__ import annotations

import os
import sys
import xml.sax.saxutils as saxutils
from collections import defaultdict

# ---------------------------------------------------------------------------
# Eras: (defName, order, techLevel, cost band) -- must match EraDefs/Eras.xml exactly.
# ---------------------------------------------------------------------------

ERAS = [
    ("SticksAndStones", 0, "Neolithic", 100, 300),
    ("Agrarian", 1, "Neolithic", 250, 600),
    ("Bronze", 2, "Medieval", 500, 1000),
    ("Classical", 3, "Medieval", 800, 1500),
    ("Medieval", 4, "Medieval", 1200, 2200),
    ("Industrial", 5, "Industrial", 2000, 3600),
    ("Information", 6, "Spacer", 3200, 5200),
    ("Exotic", 7, "Archotech", 4800, 9000),
]
ERA_ORDER = {e[0]: e[1] for e in ERAS}
ERA_TECH_LEVEL = {e[0]: e[2] for e in ERAS}
ERA_COST_BAND = {e[0]: (e[3], e[4]) for e in ERAS}
ERA_NAMES_BY_ORDER = [e[0] for e in sorted(ERAS, key=lambda e: e[1])]
MIN_PROJECTS_PER_ERA = 18
MIDDLE_ERA_MIN = 25
MIDDLE_ERAS = {"Bronze", "Classical", "Medieval"}  # of the 8, these three carry the deepest branching

# ---------------------------------------------------------------------------
# Tracks: societal-evolution threads a project belongs to. Row is the tree's vertical axis.
# ---------------------------------------------------------------------------

TRACKS = [
    ("SurvivalFood", 0),
    ("MaterialsCrafts", 1),
    ("ConstructionSettlement", 2),
    ("AgricultureAnimals", 3),
    ("SocietyGovernance", 4),
    ("KnowledgeWriting", 5),
    ("BeliefCulture", 6),
    ("Warfare", 7),
    ("TradeEconomy", 8),
    ("MedicineHealth", 9),
    ("EnergyIndustry", 10),
    ("TransportExploration", 11),
    ("InformationComputation", 12),
    ("FrontierExotic", 13),
]
TRACK_ROW = {t[0]: t[1] for t in TRACKS}
KNOWN_TRACKS = set(TRACK_ROW)

# Discovery letters kept from the original 30-project seed tree; nothing else needs one.
LETTERS = {
    "Fire": ("Fire", "Your people have learned to keep a flame burning. Cooking, warmth, and a hundred crafts follow from it."),
    "Agriculture": ("Agriculture", "Your people have learned to farm. A settlement can now feed more people than it could ever hunt or gather for."),
    "Writing": ("Writing", "Your people can now set words down. Law, ledgers, and lasting knowledge become possible."),
    "Steel": ("Steel", "Your smiths can now make steel. It will remain the backbone of tools and arms until the industrial age."),
}

# ---------------------------------------------------------------------------
# The tree. One dict per project: defName, label, track, prereqs (defNames), desc.
# Grouped by era below; era membership is the grouping itself, not a per-row field.
# A project with prereqs=[] is a root -- only SticksAndStones (the first era) has any.
# Costs are not authored by hand: they fall out of each project's depth in the DAG,
# scaled into its era's cost band (see assign_costs). tags = [era, track] plus "Frontier"
# for every Exotic-era project (all of them are speculative frontier tech).
# ---------------------------------------------------------------------------

ERA_STICKSANDSTONES = [
    dict(defName="Fire", label="fire", track="SurvivalFood", prereqs=[],
         desc="Tamed flame: warmth, light, and the first tool that is not held in the hand."),
    dict(defName="StoneTools", label="stone tools", track="MaterialsCrafts", prereqs=[],
         desc="Knapped edges: the first tools harder than bone or wood."),
    dict(defName="Language", label="language", track="KnowledgeWriting", prereqs=[],
         desc="Shared words: the first technology that lives entirely between people."),
    dict(defName="Foraging", label="foraging", track="SurvivalFood", prereqs=[],
         desc="Knowing which roots, nuts, and greens are food: a full-time diet without a single tool."),
    dict(defName="Cooking", label="cooking", track="SurvivalFood", prereqs=["Fire"],
         desc="Fire turns raw food safe and food scraps into meals."),
    dict(defName="Hunting", label="hunting", track="SurvivalFood", prereqs=["StoneTools"],
         desc="Stone points on spears: the hunt reaches farther and hits harder."),
    dict(defName="Fishing", label="fishing", track="SurvivalFood", prereqs=["StoneTools"],
         desc="Stone hooks and spears turn rivers and shorelines into a second larder."),
    dict(defName="Woodworking", label="woodworking", track="MaterialsCrafts", prereqs=["StoneTools"],
         desc="Stone axes and adzes turn timber into shaped lumber."),
    dict(defName="BoneCarving", label="bone carving", track="MaterialsCrafts", prereqs=["Hunting"],
         desc="Antler and bone, ground and shaped: needles, awls, and points stone cannot make."),
    dict(defName="Cordage", label="cordage", track="MaterialsCrafts", prereqs=["StoneTools"],
         desc="Twisted plant fiber makes rope: the first machine part, binding every craft that follows."),
    dict(defName="PitDwellings", label="pit dwellings", track="ConstructionSettlement", prereqs=["Woodworking"],
         desc="A dug floor and a timber roof: shelter that shrugs off wind no tent can stop."),
    dict(defName="WindbreakShelters", label="windbreak shelters", track="ConstructionSettlement", prereqs=["PitDwellings"],
         desc="Angled walls of hide and branch turn a campsite into a place worth returning to."),
    dict(defName="TribalCustom", label="tribal custom", track="SocietyGovernance", prereqs=["Language"],
         desc="Unwritten rules, held in speech and memory, that settle disputes before they become blood feuds."),
    dict(defName="Storytelling", label="storytelling", track="KnowledgeWriting", prereqs=["Language"],
         desc="Spoken history: knowledge that outlives the person who learned it."),
    dict(defName="AncestorVeneration", label="ancestor veneration", track="BeliefCulture", prereqs=["Language"],
         desc="The dead are remembered by name: a people's first claim to a past longer than any one life."),
    dict(defName="BodyAdornment", label="body adornment", track="BeliefCulture", prereqs=["StoneTools"],
         desc="Ochre, shell, and tooth worn on the body: the first signs that mean something beyond use."),
    dict(defName="TribalWarfare", label="tribal warfare", track="Warfare", prereqs=["StoneTools"],
         desc="Organized raiding between bands: the hunt's tools turned on other people."),
    dict(defName="GiftExchange", label="gift exchange", track="TradeEconomy", prereqs=["Language"],
         desc="A negotiated gift, given with the expectation of one returned: trade before markets."),
    dict(defName="HerbalRemedies", label="herbal remedies", track="MedicineHealth", prereqs=["Foraging"],
         desc="Foraged knowledge of which plants ease pain, close wounds, or poison an enemy."),
    dict(defName="Rafts", label="rafts", track="TransportExploration", prereqs=["Woodworking"],
         desc="Lashed logs float a load a swimmer could never carry across a river."),
]

ERA_AGRARIAN = [
    dict(defName="Trapping", label="trapping", track="SurvivalFood", prereqs=["Hunting"],
         desc="Snares and deadfalls hunt while the hunter does something else."),
    dict(defName="Pottery", label="pottery", track="MaterialsCrafts", prereqs=["Agriculture"],
         desc="Fired clay: vessels that hold water, grain, and food through the seasons."),
    dict(defName="FoodStorage", label="food storage", track="SurvivalFood", prereqs=["Pottery"],
         desc="Sealed jars and pits turn one good harvest into food for the whole lean season."),
    dict(defName="FoodPreservation", label="food preservation", track="SurvivalFood", prereqs=["FoodStorage"],
         desc="Smoking, drying, and salting keep meat and fish edible long after the kill."),
    dict(defName="Brewing", label="brewing", track="SurvivalFood", prereqs=["Agriculture"],
         desc="Grain left wet in a jar ferments into a drink safer than the water around it."),
    dict(defName="Weaving", label="weaving", track="MaterialsCrafts", prereqs=["Agriculture"],
         desc="Fiber turned to thread, thread turned to cloth."),
    dict(defName="Basketry", label="basketry", track="MaterialsCrafts", prereqs=["Woodworking"],
         desc="Split reed and withy woven tight enough to carry grain, or even water."),
    dict(defName="Tanning", label="tanning", track="MaterialsCrafts", prereqs=["Hunting"],
         desc="Cured hide resists rot and cold in a way raw skin never could."),
    dict(defName="Shelter", label="shelter", track="ConstructionSettlement", prereqs=["Woodworking"],
         desc="Framed walls and a roof: the first buildings meant to last."),
    dict(defName="Villages", label="villages", track="ConstructionSettlement", prereqs=["Shelter"],
         desc="Clustered houses around a shared well and hearth: a settlement, not a camp."),
    dict(defName="Granaries", label="granaries", track="ConstructionSettlement", prereqs=["Pottery"],
         desc="A dedicated building for grain turns a harvest into a settlement's reserve."),
    dict(defName="Agriculture", label="agriculture", track="AgricultureAnimals", prereqs=["StoneTools"],
         desc="Deliberate planting turns foraging into farming."),
    dict(defName="Irrigation", label="irrigation", track="AgricultureAnimals", prereqs=["Agriculture"],
         desc="Channeled water turns dry ground into farmland."),
    dict(defName="AnimalHusbandry", label="animal husbandry", track="AgricultureAnimals", prereqs=["Agriculture"],
         desc="Herds kept and bred, rather than only hunted."),
    dict(defName="SelectiveBreeding", label="selective breeding", track="AgricultureAnimals", prereqs=["AnimalHusbandry"],
         desc="Choosing which animals sire the next generation shapes a herd on purpose."),
    dict(defName="ChiefdomLeadership", label="chiefdom leadership", track="SocietyGovernance", prereqs=["TribalCustom", "Villages"],
         desc="One household's authority, passed down within it, over a village that no longer fits around a fire."),
    dict(defName="LandTenure", label="land tenure", track="SocietyGovernance", prereqs=["Villages"],
         desc="A field belongs to whoever cleared and planted it, and that claim outlives the season."),
    dict(defName="Writing", label="writing", track="KnowledgeWriting", prereqs=["Storytelling", "Pottery"],
         desc="Marks that hold speech: a story no longer needs a storyteller to survive."),
    dict(defName="AnimisticReligion", label="animistic religion", track="BeliefCulture", prereqs=["AncestorVeneration"],
         desc="Spirits bound to field, river, and herd: belief that grows to match a farmed landscape."),
    dict(defName="Music", label="music", track="BeliefCulture", prereqs=["Storytelling"],
         desc="Rhythm and melody carry a story further, and hold a crowd's attention longer, than words alone."),
    dict(defName="Bows", label="bows", track="Warfare", prereqs=["Hunting"],
         desc="A drawn bow strikes harder and from farther than any thrown spear."),
    dict(defName="Slings", label="slings", track="Warfare", prereqs=["StoneTools"],
         desc="A whirled sling throws a stone hard enough to break bone at a distance."),
    dict(defName="Barter", label="barter", track="TradeEconomy", prereqs=["GiftExchange"],
         desc="Grain for pottery, pottery for hides: goods exchanged by haggled agreement, not obligation."),
    dict(defName="WoundCare", label="wound care", track="MedicineHealth", prereqs=["HerbalRemedies"],
         desc="Bandaging, splinting, and cleaning a wound turn an injury into something survivable."),
    dict(defName="PackAnimals", label="pack animals", track="TransportExploration", prereqs=["AnimalHusbandry"],
         desc="A laden ox or donkey carries what ten people would have to shoulder."),
]

ERA_BRONZE = [
    dict(defName="Beekeeping", label="beekeeping", track="SurvivalFood", prereqs=["AnimalHusbandry"],
         desc="Kept hives yield honey on a schedule instead of by lucky discovery."),
    dict(defName="Winemaking", label="winemaking", track="SurvivalFood", prereqs=["Brewing"],
         desc="Pressed and fermented grapes turn an orchard crop into a trade good that keeps for years."),
    dict(defName="Copper", label="copper", track="MaterialsCrafts", prereqs=["Agriculture"],
         desc="The first worked metal: soft, but no longer stone."),
    dict(defName="Bronze", label="bronze", track="MaterialsCrafts", prereqs=["Copper"],
         desc="Copper alloyed with tin: an age takes its name from it."),
    dict(defName="Glassmaking", label="glassmaking", track="MaterialsCrafts", prereqs=["Pottery"],
         desc="Kiln heat fuses sand and ash into a material stone and clay never made: transparent, and reusable."),
    dict(defName="Dyeing", label="dyeing", track="MaterialsCrafts", prereqs=["Weaving"],
         desc="Plant and mineral pigment fixed into cloth turns a bolt of wool into a marker of status."),
    dict(defName="Wheel", label="wheel", track="TransportExploration", prereqs=["Bronze"],
         desc="A rolling load moves for a fraction of the effort of a dragged one."),
    dict(defName="Cart", label="cart", track="TransportExploration", prereqs=["Wheel"],
         desc="A wheeled frame: goods move by animal power instead of by back."),
    dict(defName="Sailing", label="sailing", track="TransportExploration", prereqs=["Woodworking", "Bronze"],
         desc="Wind-driven hulls: rivers and coastlines become roads."),
    dict(defName="Masonry", label="masonry", track="ConstructionSettlement", prereqs=["Wheel"],
         desc="Cut and fitted stone: walls that outlast the people who raised them."),
    dict(defName="Fortification", label="fortification", track="ConstructionSettlement", prereqs=["Masonry"],
         desc="A wall and a ditch turn a village into something an enemy has to besiege, not just raid."),
    dict(defName="UrbanPlanning", label="urban planning", track="ConstructionSettlement", prereqs=["Villages", "Masonry"],
         desc="Streets laid out before the houses are built let a settlement grow into a city on purpose."),
    dict(defName="Plow", label="plow", track="AgricultureAnimals", prereqs=["AnimalHusbandry", "Bronze"],
         desc="An animal-drawn blade turns soil faster and deeper than any hoe."),
    dict(defName="Pastoralism", label="pastoralism", track="AgricultureAnimals", prereqs=["SelectiveBreeding"],
         desc="Herds moved with the seasons across grassland too dry to farm."),
    dict(defName="Kingship", label="kingship", track="SocietyGovernance", prereqs=["ChiefdomLeadership", "UrbanPlanning"],
         desc="One ruling house, its claim inherited rather than earned, over a city too large to govern by council."),
    dict(defName="Bureaucracy", label="bureaucracy", track="SocietyGovernance", prereqs=["Writing"],
         desc="Scribes who record grain, taxes, and decrees let a ruler govern people they never meet."),
    dict(defName="Mathematics", label="mathematics", track="KnowledgeWriting", prereqs=["Writing"],
         desc="Written numbers: measurement, accounting, and the geometry that follows."),
    dict(defName="Calendars", label="calendars", track="KnowledgeWriting", prereqs=["Writing"],
         desc="A written count of days and seasons tells a farmer when to plant before the sky does."),
    dict(defName="Priesthood", label="priesthood", track="BeliefCulture", prereqs=["AnimisticReligion", "Kingship"],
         desc="A dedicated class of ritual specialists, housed in temples a king helps pay for."),
    dict(defName="Mythology", label="mythology", track="BeliefCulture", prereqs=["Music", "Writing"],
         desc="Written epics fix a people's gods and heroes into a shared, unchanging story."),
    dict(defName="BronzeWeapons", label="bronze weapons", track="Warfare", prereqs=["Bronze"],
         desc="A cast bronze blade holds an edge no sharpened stone ever could."),
    dict(defName="Chariots", label="chariots", track="Warfare", prereqs=["Cart", "BronzeWeapons"],
         desc="A light cart pulled at a gallop turns a footbound raid into a shock charge."),
    dict(defName="Phalanx", label="phalanx tactics", track="Warfare", prereqs=["BronzeWeapons"],
         desc="Spearmen who hold a shield wall together fight as one weapon, not many."),
    dict(defName="LongDistanceTrade", label="long-distance trade", track="TradeEconomy", prereqs=["Barter", "Sailing"],
         desc="Caravan and coastal routes carry goods far past the reach of any one market day."),
    dict(defName="Currency", label="currency", track="TradeEconomy", prereqs=["Bronze", "Writing"],
         desc="Standard coin replaces barter as the measure of a trade."),
    dict(defName="Midwifery", label="midwifery", track="MedicineHealth", prereqs=["WoundCare"],
         desc="Practiced technique around childbirth turns a dangerous moment into a survivable one, more often."),
    dict(defName="Kilns", label="kilns", track="EnergyIndustry", prereqs=["Pottery"],
         desc="Kilns that hold 1000°C turn clay into watertight vessels and open the road to metals."),
]

ERA_CLASSICAL = [
    dict(defName="Aquaculture", label="aquaculture", track="SurvivalFood", prereqs=["Fishing", "Irrigation"],
         desc="Fish penned in irrigated ponds turn a river's bounty into a managed crop."),
    dict(defName="Iron", label="iron", track="MaterialsCrafts", prereqs=["Bronze", "Masonry"],
         desc="Smelted iron: harder and more common than bronze ever was."),
    dict(defName="Bellows", label="bellows", track="MaterialsCrafts", prereqs=["Bronze"],
         desc="A worked bellows forces air into a forge fire, reaching heats a mouth-blown flame never could."),
    dict(defName="Enamelling", label="enamelling", track="MaterialsCrafts", prereqs=["Glassmaking"],
         desc="Fused glass fired onto metal or clay adds color and a surface nothing else can scratch."),
    dict(defName="Aqueduct", label="aqueduct", track="ConstructionSettlement", prereqs=["Masonry", "Irrigation"],
         desc="Carried water: a city can grow far past what its own wells could support."),
    dict(defName="Roadbuilding", label="roadbuilding", track="ConstructionSettlement", prereqs=["Masonry", "Cart"],
         desc="Paved roads: a cart moves as fast in the rain as in the sun."),
    dict(defName="MonumentalArchitecture", label="monumental architecture", track="ConstructionSettlement", prereqs=["Masonry", "Mathematics"],
         desc="Calculated proportion and cut stone raise temples and tombs meant to outlast empires."),
    dict(defName="Sanitation", label="sanitation", track="ConstructionSettlement", prereqs=["Aqueduct"],
         desc="Covered sewers carry waste out of a city before it can carry disease back in."),
    dict(defName="Terracing", label="terracing", track="AgricultureAnimals", prereqs=["Irrigation", "Masonry"],
         desc="Stepped, walled fields turn a hillside too steep to plow into farmland."),
    dict(defName="Horticulture", label="horticulture", track="AgricultureAnimals", prereqs=["Irrigation"],
         desc="Deliberately tended orchards and gardens produce fruit no wild stand ever yielded so reliably."),
    dict(defName="CodifiedLaw", label="codified law", track="SocietyGovernance", prereqs=["Bureaucracy", "Philosophy"],
         desc="Written law applies the same to every case, not to whichever noble hears it that day."),
    dict(defName="Citizenship", label="citizenship", track="SocietyGovernance", prereqs=["CodifiedLaw"],
         desc="A defined body of citizens, with rights a king or council cannot simply revoke."),
    dict(defName="Taxation", label="taxation", track="SocietyGovernance", prereqs=["Bureaucracy", "Currency"],
         desc="Coined tax, recorded and collected on schedule, funds armies and roads no single harvest could."),
    dict(defName="Astronomy", label="astronomy", track="KnowledgeWriting", prereqs=["Mathematics", "Calendars"],
         desc="Tracking the sky: calendars, navigation, and the shape of the year."),
    dict(defName="Geometry", label="geometry", track="KnowledgeWriting", prereqs=["Mathematics"],
         desc="Proven relationships between shapes let a builder measure a wall before it exists."),
    dict(defName="Libraries", label="libraries", track="KnowledgeWriting", prereqs=["Writing", "MonumentalArchitecture"],
         desc="A building dedicated to storing written scrolls turns scattered knowledge into a shared archive."),
    dict(defName="Philosophy", label="philosophy", track="BeliefCulture", prereqs=["Writing", "Storytelling"],
         desc="Written argument turns storytelling into systematic thought."),
    dict(defName="OrganizedReligion", label="organized religion", track="BeliefCulture", prereqs=["Priesthood", "Mythology"],
         desc="A settled hierarchy of temples and clergy turns scattered ritual into a single institution."),
    dict(defName="Theater", label="theater", track="BeliefCulture", prereqs=["Mythology"],
         desc="Myth performed before a paying crowd becomes a civic ritual in its own right."),
    dict(defName="IronWeapons", label="iron weapons", track="Warfare", prereqs=["Iron"],
         desc="Iron blades are cheaper to arm an army with than bronze ever was, and just as sharp."),
    dict(defName="SiegeEngines", label="siege engines", track="Warfare", prereqs=["IronWeapons", "MonumentalArchitecture"],
         desc="Torsion and counterweight turn stored energy into a wall-breaking blow."),
    dict(defName="StandingArmies", label="standing armies", track="Warfare", prereqs=["Phalanx", "Bureaucracy"],
         desc="Soldiers paid and drilled year-round, not called up from the fields each campaign season."),
    dict(defName="TradeRoutes", label="trade routes", track="TradeEconomy", prereqs=["LongDistanceTrade", "Roadbuilding"],
         desc="Maintained roads and sea lanes turn occasional caravans into a standing network."),
    dict(defName="Markets", label="markets", track="TradeEconomy", prereqs=["Currency"],
         desc="A fixed, regulated place to trade turns haggling into a public institution with its own rules."),
    dict(defName="Medicine", label="medicine", track="MedicineHealth", prereqs=["Writing", "Philosophy"],
         desc="Written case and remedy: healing becomes knowledge passed on, not just skill."),
    dict(defName="Surgery", label="surgery", track="MedicineHealth", prereqs=["Medicine"],
         desc="Deliberate cutting, guided by written precedent, treats what a poultice alone cannot."),
    dict(defName="PublicHygiene", label="public hygiene", track="MedicineHealth", prereqs=["Medicine", "Sanitation"],
         desc="Written medical guidance about clean water and waste turns sanitation into public policy."),
    dict(defName="WaterMills", label="water mills", track="EnergyIndustry", prereqs=["Wheel", "Masonry"],
         desc="A river turning a wheel grinds grain all day without a single tired arm."),
    dict(defName="Navigation", label="navigation", track="TransportExploration", prereqs=["Sailing", "Astronomy"],
         desc="Star sightings and dead reckoning let a captain find a coast without ever seeing it first."),
    dict(defName="Caravans", label="caravans", track="TransportExploration", prereqs=["Cart", "PackAnimals"],
         desc="Carts and pack animals traveling together cross deserts no lone trader would risk."),
]

ERA_MEDIEVAL = [
    dict(defName="Distillation", label="distillation", track="SurvivalFood", prereqs=["Brewing"],
         desc="Boiling and recondensing a ferment concentrates it into spirits far stronger than any brew."),
    dict(defName="Confectionery", label="confectionery", track="SurvivalFood", prereqs=["Beekeeping", "Winemaking"],
         desc="Honey and preserved fruit, boiled and set, turn raw sweetness into a keepable treat."),
    dict(defName="Steel", label="steel", track="MaterialsCrafts", prereqs=["Iron", "Bellows"],
         desc="Iron refined further still: the backbone of the medieval world."),
    dict(defName="MetalCasting", label="metal casting", track="MaterialsCrafts", prereqs=["Steel"],
         desc="Molten metal poured into a mold reproduces a shape exactly, as many times as needed."),
    dict(defName="Clockwork", label="clockwork", track="MaterialsCrafts", prereqs=["Steel"],
         desc="Gears and springs cut to a tolerance turn stored tension into a steady, countable motion."),
    dict(defName="Alchemy", label="alchemy", track="MaterialsCrafts", prereqs=["Bellows"],
         desc="Systematic experiment with heat, acid, and mineral, chasing transmutation and finding real chemistry by accident."),
    dict(defName="Castles", label="castles", track="ConstructionSettlement", prereqs=["Fortification", "Steel"],
         desc="Steel-reinforced stone keeps turn fortification into a fortress a siege engine struggles to crack."),
    dict(defName="Cathedrals", label="cathedrals", track="ConstructionSettlement", prereqs=["MonumentalArchitecture", "Philosophy"],
         desc="Ribbed vaults and flying buttresses let stone walls climb higher than gravity should allow."),
    dict(defName="GuildHalls", label="guild halls", track="ConstructionSettlement", prereqs=["UrbanPlanning", "Currency"],
         desc="A dedicated hall gives a city's craftsmen a place to set standards and settle disputes."),
    dict(defName="HeavyPlow", label="heavy plow", track="AgricultureAnimals", prereqs=["Plow", "Steel"],
         desc="A steel-tipped, wheeled plow turns heavy clay soil that a wooden blade could never break."),
    dict(defName="ThreeFieldSystem", label="three-field system", track="AgricultureAnimals", prereqs=["HeavyPlow", "AnimalHusbandry"],
         desc="Rotating a third field to legumes each year rests the soil without resting the harvest."),
    dict(defName="Feudalism", label="feudalism", track="SocietyGovernance", prereqs=["Kingship", "Castles"],
         desc="Land granted in exchange for loyalty and arms lets a king rule through his own nobles."),
    dict(defName="GuildSystem", label="guild system", track="SocietyGovernance", prereqs=["GuildHalls", "Taxation"],
         desc="Chartered guilds regulate a trade's training, quality, and price across an entire city."),
    dict(defName="CanonLaw", label="canon law", track="SocietyGovernance", prereqs=["CodifiedLaw", "Monasticism"],
         desc="A written legal code run by the church stands alongside, and sometimes above, a king's own courts."),
    dict(defName="Paper", label="paper", track="KnowledgeWriting", prereqs=["Weaving", "Libraries"],
         desc="Pulped fiber pressed thin and dried is cheaper than parchment and just as good to write on."),
    dict(defName="Algebra", label="algebra", track="KnowledgeWriting", prereqs=["Geometry"],
         desc="Solving for an unknown by symbol, not just by shape, generalizes mathematics past geometry's reach."),
    dict(defName="Scholasticism", label="scholasticism", track="KnowledgeWriting", prereqs=["Libraries", "Philosophy"],
         desc="Universities built around a library and a curriculum turn scattered philosophy into a formal course of study."),
    dict(defName="Monasticism", label="monasticism", track="BeliefCulture", prereqs=["OrganizedReligion"],
         desc="Communities bound by vow and rule keep copying, farming, and praying long after a kingdom falls."),
    dict(defName="Chivalry", label="chivalry", track="BeliefCulture", prereqs=["Feudalism", "Philosophy"],
         desc="A written code of conduct binds an armored noble's violence to some notion of honor."),
    dict(defName="SteelWeapons", label="steel weapons", track="Warfare", prereqs=["Steel"],
         desc="A steel blade holds a keener edge, and survives more of them, than iron ever managed."),
    dict(defName="Crossbows", label="crossbows", track="Warfare", prereqs=["SteelWeapons"],
         desc="A steel lath and a trigger let an untrained conscript hold the draw of a master archer."),
    dict(defName="Knighthood", label="knighthood", track="Warfare", prereqs=["SteelWeapons", "Chivalry"],
         desc="An armored horseman, bound by chivalric code and backed by his own land, becomes war's elite."),
    dict(defName="Gunpowder", label="gunpowder", track="Warfare", prereqs=["Alchemy"],
         desc="Saltpeter, sulfur, and charcoal, mixed and packed, release their stored energy all at once."),
    dict(defName="Banking", label="banking", track="TradeEconomy", prereqs=["Currency", "Writing"],
         desc="Written letters of credit let a merchant move wealth across a continent without moving coin."),
    dict(defName="GuildTrade", label="guild trade", track="TradeEconomy", prereqs=["Markets", "GuildSystem"],
         desc="Chartered guilds fix prices and quality across market towns, turning local trade into a regulated network."),
    dict(defName="DoubleEntryBookkeeping", label="double-entry bookkeeping", track="TradeEconomy", prereqs=["Banking"],
         desc="Recording every transaction twice, as a debit and a credit, catches an error a single ledger line would hide."),
    dict(defName="Hospitals", label="hospitals", track="MedicineHealth", prereqs=["Surgery", "Monasticism"],
         desc="A building dedicated to the sick, staffed and provisioned, treats more people than any single healer could."),
    dict(defName="Anatomy", label="anatomy", track="MedicineHealth", prereqs=["Surgery"],
         desc="Systematic dissection maps the body's structure well enough to teach, not just to guess at."),
    dict(defName="Pharmacology", label="pharmacology", track="MedicineHealth", prereqs=["HerbalRemedies", "Alchemy"],
         desc="Alchemical distillation and measured dosing turn folk remedy into a reproducible medicine."),
    dict(defName="Windmills", label="windmills", track="EnergyIndustry", prereqs=["WaterMills"],
         desc="Sails on a turning cap catch wind a river's current never offered, and grind grain anywhere it blows."),
    dict(defName="Compass", label="compass", track="TransportExploration", prereqs=["Navigation"],
         desc="A floating lodestone always points north, in fog or open ocean, when the stars are hidden."),
    dict(defName="OceanicNavigation", label="oceanic navigation", track="TransportExploration", prereqs=["Compass"],
         desc="Compass, chart, and reckoning together let a captain leave sight of any coast at all."),
    dict(defName="Shipbuilding", label="shipbuilding", track="TransportExploration", prereqs=["OceanicNavigation", "Steel"],
         desc="Steel-fastened, multi-decked hulls survive ocean crossings a coastal galley never could."),
]

ERA_INDUSTRIAL = [
    dict(defName="Canning", label="canning", track="SurvivalFood", prereqs=["FoodPreservation", "TinPlating"],
         desc="Food sealed airtight in a tin-plated can outlasts salting or smoking by years, not seasons."),
    dict(defName="FoodRefrigeration", label="food refrigeration", track="SurvivalFood", prereqs=["Canning", "Electricity"],
         desc="Mechanical cold storage keeps fresh food edible without drying, salting, or sealing it at all."),
    dict(defName="CokeSmelting", label="coke smelting", track="MaterialsCrafts", prereqs=["Steel"],
         desc="Coke burns hotter and cleaner than charcoal, smelting iron at a scale charcoal forests could never fuel."),
    dict(defName="TinPlating", label="tin plating", track="MaterialsCrafts", prereqs=["MetalCasting"],
         desc="A thin tin coat on rolled steel resists rust long enough to hold food safely."),
    dict(defName="Vulcanization", label="vulcanization", track="MaterialsCrafts", prereqs=["CokeSmelting"],
         desc="Sulfur cross-linked into raw rubber under heat turns a sticky curiosity into a durable material."),
    dict(defName="BessemerProcess", label="bessemer process", track="MaterialsCrafts", prereqs=["CokeSmelting"],
         desc="Blown air burns carbon out of molten iron in minutes, turning steel from a craft into an industry."),
    dict(defName="SteelFraming", label="steel framing", track="ConstructionSettlement", prereqs=["BessemerProcess"],
         desc="A riveted steel skeleton carries a building's weight, freeing walls to be little more than a skin."),
    dict(defName="UrbanSanitation", label="urban sanitation", track="ConstructionSettlement", prereqs=["Sanitation", "SteelFraming"],
         desc="Pressurized water mains and citywide sewers serve a population no gravity-fed aqueduct ever could."),
    dict(defName="Zoning", label="zoning", track="ConstructionSettlement", prereqs=["UrbanPlanning", "SteelFraming"],
         desc="Districts set aside by law keep a factory's smoke and noise away from where people sleep."),
    dict(defName="SyntheticFertilizer", label="synthetic fertilizer", track="AgricultureAnimals", prereqs=["ThreeFieldSystem", "Thermodynamics"],
         desc="Industrially fixed nitrogen feeds a field far past what manure and rotation ever could."),
    dict(defName="MechanizedFarming", label="mechanized farming", track="AgricultureAnimals", prereqs=["HeavyPlow", "SteamEngine"],
         desc="A steam-driven thresher does the work of a harvest crew, and never tires."),
    dict(defName="NationState", label="nation state", track="SocietyGovernance", prereqs=["Feudalism", "Citizenship"],
         desc="A single sovereign authority over a defined territory replaces overlapping feudal loyalties."),
    dict(defName="Constitutionalism", label="constitutionalism", track="SocietyGovernance", prereqs=["Citizenship", "NationState"],
         desc="A written constitution binds even the nation's rulers to law, not just its subjects."),
    dict(defName="CivilService", label="civil service", track="SocietyGovernance", prereqs=["Bureaucracy", "NationState"],
         desc="Officials hired and promoted on examined merit run a nation's business past any one ruler's reign."),
    dict(defName="Printing", label="printing", track="KnowledgeWriting", prereqs=["Paper", "MetalCasting"],
         desc="Cast metal type, reused page after page, turns one copied book into a thousand identical ones."),
    dict(defName="ScientificMethod", label="scientific method", track="KnowledgeWriting", prereqs=["Scholasticism", "Printing"],
         desc="Published, repeatable experiment replaces inherited authority as the test of what is true."),
    dict(defName="Encyclopedism", label="encyclopedism", track="KnowledgeWriting", prereqs=["Printing", "Libraries"],
         desc="A printed, cross-referenced compendium puts a library's worth of knowledge on a single shelf."),
    dict(defName="Reformation", label="reformation", track="BeliefCulture", prereqs=["Monasticism", "Printing"],
         desc="Printed scripture in a common tongue lets belief split from a single church's authority."),
    dict(defName="Humanism", label="humanism", track="BeliefCulture", prereqs=["Reformation", "ScientificMethod"],
         desc="Human reason and dignity, not divine decree alone, become a legitimate ground for belief."),
    dict(defName="Firearms", label="firearms", track="Warfare", prereqs=["Gunpowder", "BessemerProcess"],
         desc="A cast steel barrel turns gunpowder's blast into an aimed shot instead of a loud accident."),
    dict(defName="Artillery", label="artillery", track="Warfare", prereqs=["Firearms", "SteelFraming"],
         desc="A steel-cast cannon throws a shell farther and harder than any siege engine's spring or weight."),
    dict(defName="Ironclads", label="ironclads", track="Warfare", prereqs=["Firearms", "SteamEngine", "CokeSmelting"],
         desc="Steam power and armor plate turn a warship into something cannon fire can no longer easily sink."),
    dict(defName="StockExchanges", label="stock exchanges", track="TradeEconomy", prereqs=["Banking", "DoubleEntryBookkeeping"],
         desc="Shares of a venture, bought and sold in one open hall, spread its risk across many strangers at once."),
    dict(defName="Insurance", label="insurance", track="TradeEconomy", prereqs=["StockExchanges"],
         desc="Pooled premiums let a single lost ship be a manageable loss, not a ruined merchant."),
    dict(defName="IndustrialCapitalism", label="industrial capitalism", track="TradeEconomy", prereqs=["StockExchanges", "SteelFraming"],
         desc="Traded capital funds steel-framed factories at a scale no single merchant fortune could finance alone."),
    dict(defName="Antiseptics", label="antiseptics", track="MedicineHealth", prereqs=["Anatomy", "Pharmacology"],
         desc="Chemically killing germs on a wound, not just cleaning it, cuts infection deaths sharply."),
    dict(defName="Anesthesia", label="anesthesia", track="MedicineHealth", prereqs=["Antiseptics"],
         desc="A measured dose of ether or chloroform lets a surgeon operate on a patient who feels nothing."),
    dict(defName="Microscope", label="microscope", track="MedicineHealth", prereqs=["Glassmaking", "Anatomy"],
         desc="Ground lenses stacked in a tube reveal a world of living things too small for the naked eye."),
    dict(defName="CoalMining", label="coal mining", track="EnergyIndustry", prereqs=["Kilns"],
         desc="Deep-shaft coal mining supplies a fuel denser and more abundant than any forest could regrow."),
    dict(defName="SteamEngine", label="steam engine", track="EnergyIndustry", prereqs=["CoalMining", "CokeSmelting"],
         desc="Burning coal to boil water and drive a piston delivers power no river or windmill site can limit."),
    dict(defName="Thermodynamics", label="thermodynamics", track="EnergyIndustry", prereqs=["SteamEngine"],
         desc="A theory of heat and work explains why an engine loses power, and how to claw some of it back."),
    dict(defName="Electricity", label="electricity", track="EnergyIndustry", prereqs=["Thermodynamics"],
         desc="A generator turns mechanical motion into a current that travels down a wire faster than any belt."),
    dict(defName="Railways", label="railways", track="TransportExploration", prereqs=["SteamEngine", "SteelFraming"],
         desc="A steam engine on steel rails hauls more freight, faster, than any road ever carried."),
    dict(defName="SteamShips", label="steam ships", track="TransportExploration", prereqs=["Shipbuilding", "SteamEngine"],
         desc="A steam-driven hull keeps schedule against wind and tide, where a sailing ship only guesses at both."),
    dict(defName="BooleanLogic", label="boolean logic", track="InformationComputation", prereqs=["Algebra"],
         desc="Algebra rewritten for true and false lets any argument, in principle, be checked like a sum."),
    dict(defName="Telegraphy", label="telegraphy", track="InformationComputation", prereqs=["Electricity"],
         desc="A coded electric pulse down a wire carries a message across a continent in minutes, not weeks."),
]

ERA_INFORMATION = [
    dict(defName="FoodScience", label="food science", track="SurvivalFood", prereqs=["FoodRefrigeration", "Biochemistry"],
         desc="Understanding food at the level of chemistry, not just heat and salt, engineers what keeps and what spoils."),
    dict(defName="VerticalFarming", label="vertical farming", track="SurvivalFood", prereqs=["Hydroponics", "Computers"],
         desc="Stacked, computer-tended growing trays put a field's yield inside a single climate-controlled building."),
    dict(defName="Plastics", label="plastics", track="MaterialsCrafts", prereqs=["Vulcanization", "Petrochemistry"],
         desc="Refined petroleum chains link into polymers moldable into shapes no natural material takes so easily."),
    dict(defName="Alloys", label="advanced alloys", track="MaterialsCrafts", prereqs=["BessemerProcess"],
         desc="Precisely blended trace metals give steel and its rivals strength or lightness far past a plain smelt."),
    dict(defName="CompositeMaterials", label="composite materials", track="MaterialsCrafts", prereqs=["Plastics", "Alloys"],
         desc="Fiber and resin layered together outperform either the metal or the plastic alone."),
    dict(defName="Skyscrapers", label="skyscrapers", track="ConstructionSettlement", prereqs=["SteelFraming", "Alloys"],
         desc="A lightweight alloy frame and a fast elevator let a building climb past what masonry could ever hold up."),
    dict(defName="Megacities", label="megacities", track="ConstructionSettlement", prereqs=["Zoning", "Skyscrapers"],
         desc="Zoned districts of towering density let a single city hold a population once spread across a nation."),
    dict(defName="Hydroponics", label="hydroponics", track="AgricultureAnimals", prereqs=["SyntheticFertilizer"],
         desc="Roots fed a precise nutrient solution grow a crop with no soil at all."),
    dict(defName="GeneticSelection", label="genetic selection", track="AgricultureAnimals", prereqs=["SelectiveBreeding", "Biochemistry"],
         desc="Reading a lineage's genes, not just its traits, breeds a crop or herd toward a target on purpose."),
    dict(defName="Democracy", label="democracy", track="SocietyGovernance", prereqs=["Constitutionalism"],
         desc="Universal suffrage extends the constitution's promise from a propertied few to every citizen."),
    dict(defName="WelfareState", label="welfare state", track="SocietyGovernance", prereqs=["Democracy", "CivilService"],
         desc="A voted mandate and a professional civil service fund healthcare and pensions as a public guarantee."),
    dict(defName="MassEducation", label="mass education", track="KnowledgeWriting", prereqs=["Encyclopedism", "Democracy"],
         desc="Compulsory, publicly funded schooling makes literacy a citizen's right, not a scholar's privilege."),
    dict(defName="PublicLibraries", label="public libraries", track="KnowledgeWriting", prereqs=["MassEducation", "Encyclopedism"],
         desc="A library open to every citizen, not just scholars, makes a printed encyclopedia's knowledge common property."),
    dict(defName="SecularEthics", label="secular ethics", track="BeliefCulture", prereqs=["Humanism", "Democracy"],
         desc="A framework of right and wrong argued from reason and consent, not scripture, guides a citizen who votes."),
    dict(defName="MassMedia", label="mass media", track="BeliefCulture", prereqs=["SecularEthics", "Electricity"],
         desc="Radio, film, and the wire service put the same story in front of a whole nation at once."),
    dict(defName="MachineGuns", label="machine guns", track="Warfare", prereqs=["Firearms", "Alloys"],
         desc="A mechanism that reloads itself turns one soldier's aim into an unbroken line of fire."),
    dict(defName="Tanks", label="tanks", track="Warfare", prereqs=["MachineGuns", "InternalCombustion"],
         desc="An engine-driven armored hull shrugs off machine-gun fire that stops infantry cold."),
    dict(defName="Airpower", label="airpower", track="Warfare", prereqs=["Aviation", "MachineGuns"],
         desc="An armed aircraft strikes a target no trench or wall was ever built to stop from above."),
    dict(defName="GlobalMarkets", label="global markets", track="TradeEconomy", prereqs=["IndustrialCapitalism", "Aviation"],
         desc="Air freight and wired settlement let capital and goods move between continents in a single business day."),
    dict(defName="Corporations", label="corporations", track="TradeEconomy", prereqs=["IndustrialCapitalism"],
         desc="A legal person, owned by shareholders and outliving any one of them, can outgrow a family firm entirely."),
    dict(defName="GermTheory", label="germ theory", track="MedicineHealth", prereqs=["Microscope"],
         desc="Seeing the microorganisms that cause disease replaces bad air and humors with a testable cause."),
    dict(defName="Vaccination", label="vaccination", track="MedicineHealth", prereqs=["Microscope", "GermTheory"],
         desc="A weakened or dead pathogen, introduced deliberately, trains the body to fight the real thing first."),
    dict(defName="Biochemistry", label="biochemistry", track="MedicineHealth", prereqs=["GermTheory", "Pharmacology"],
         desc="Chemistry applied to the cell explains disease and remedy alike at the level of molecules."),
    dict(defName="Petrochemistry", label="petrochemistry", track="EnergyIndustry", prereqs=["Electricity"],
         desc="Refining crude oil into fuel and feedstock unlocks an energy density coal never matched."),
    dict(defName="InternalCombustion", label="internal combustion", track="EnergyIndustry", prereqs=["Petrochemistry", "Thermodynamics"],
         desc="Fuel burned inside the cylinder itself delivers a steam engine's power from an engine a person can carry."),
    dict(defName="PowerGrids", label="power grids", track="EnergyIndustry", prereqs=["Electricity"],
         desc="Transmission lines strung between a generator and a city deliver electricity to every home on the line."),
    dict(defName="NuclearFission", label="nuclear fission", track="EnergyIndustry", prereqs=["Electricity", "Alloys"],
         desc="Splitting a heavy atom's nucleus releases more energy from a handful of fuel than a mountain of coal."),
    dict(defName="AutomobileEngineering", label="automobile engineering", track="TransportExploration", prereqs=["InternalCombustion"],
         desc="A compact engine on a wheeled chassis puts a railway's speed in a single family's driveway."),
    dict(defName="Aviation", label="aviation", track="TransportExploration", prereqs=["InternalCombustion", "Alloys"],
         desc="A light alloy airframe and a combustion engine lift a craft that outruns any ship or train."),
    dict(defName="SpaceRocketry", label="space rocketry", track="TransportExploration", prereqs=["Aviation", "NuclearFission"],
         desc="A staged rocket, burning fuel fast enough, finally reaches the speed it takes to leave the atmosphere behind."),
    dict(defName="Computers", label="computers", track="InformationComputation", prereqs=["Electricity", "BooleanLogic"],
         desc="Electrically switched logic gates carry out boolean reasoning faster than any hand ever could."),
    dict(defName="Semiconductors", label="semiconductors", track="InformationComputation", prereqs=["Alloys"],
         desc="Doped crystal that switches current on command replaces a computer's fragile vacuum tubes."),
    dict(defName="Telecommunications", label="telecommunications", track="InformationComputation", prereqs=["Telegraphy", "PowerGrids"],
         desc="Amplified electric signal over a powered network carries a voice, not just a coded pulse, across a continent."),
    dict(defName="TheInternet", label="the internet", track="InformationComputation", prereqs=["Computers", "Telecommunications"],
         desc="Computers linked over a shared telecommunications network route any message between any two of them."),
    dict(defName="SpaceStations", label="space stations", track="FrontierExotic", prereqs=["SpaceRocketry"],
         desc="A rocket-assembled module in orbit lets people live and work above the atmosphere for months at a stretch."),
    dict(defName="AsteroidMining", label="asteroid mining", track="FrontierExotic", prereqs=["SpaceStations"],
         desc="An orbital foothold makes it possible to reach, and strip, a rock that never has to fight gravity to give up its ore."),
]

ERA_EXOTIC = [
    dict(defName="Nanomaterials", label="nanomaterials", track="MaterialsCrafts", prereqs=["CompositeMaterials"],
         desc="Structure engineered atom by atom gives a material strength-to-weight ratios no bulk alloy can reach."),
    dict(defName="OrbitalHabitats", label="orbital habitats", track="ConstructionSettlement", prereqs=["Megacities"],
         desc="A rotating station-sized ring simulates gravity well enough to house a city's worth of people off-world."),
    dict(defName="Arcologies", label="arcologies", track="ConstructionSettlement", prereqs=["Megacities", "CompositeMaterials"],
         desc="A single self-contained structure, built to composite tolerances, houses what once took a whole megacity."),
    dict(defName="Xenobotany", label="xenobotany", track="AgricultureAnimals", prereqs=["GeneticSelection"],
         desc="Genetic tools built for earthly crops get turned on organisms suited to soils and skies not our own."),
    dict(defName="AIAdministration", label="AI administration", track="SocietyGovernance", prereqs=["WelfareState", "Computers"],
         desc="Automated systems apply policy at a scale and consistency no human civil service ever matched."),
    dict(defName="MemeticCulture", label="memetic culture", track="BeliefCulture", prereqs=["MassMedia"],
         desc="Culture spread instantly across a networked population evolves and mutates faster than any broadcast era allowed."),
    dict(defName="DirectedEnergyWeapons", label="directed energy weapons", track="Warfare", prereqs=["Airpower", "FusionPower"],
         desc="A fusion-fed beam weapon strikes at the speed of light, with no shell or bullet to intercept."),
    dict(defName="AutonomousDrones", label="autonomous drones", track="Warfare", prereqs=["Airpower", "Computers"],
         desc="A computer-piloted aircraft flies and decides without a person aboard to risk or to hesitate."),
    dict(defName="Genomics", label="genomics", track="MedicineHealth", prereqs=["Biochemistry"],
         desc="Reading and editing a whole genome turns inherited disease from a diagnosis into a fixable defect."),
    dict(defName="LifeExtension", label="life extension", track="MedicineHealth", prereqs=["Genomics"],
         desc="Genomic repair of the mechanisms behind aging itself pushes a natural lifespan well past its old limit."),
    dict(defName="FusionPower", label="fusion power", track="EnergyIndustry", prereqs=["NuclearFission"],
         desc="Fusing light nuclei, rather than splitting heavy ones, releases more energy with far less waste."),
    dict(defName="DysonSwarmEngineering", label="dyson swarm engineering", track="EnergyIndustry", prereqs=["FusionPower"],
         desc="A swarm of orbital collectors captures a star's output directly, at a scale no planetary grid approaches."),
    dict(defName="InterplanetaryTravel", label="interplanetary travel", track="TransportExploration", prereqs=["SpaceRocketry", "FusionPower"],
         desc="A fusion-driven engine cuts a months-long transfer orbit down to a trip worth actually making."),
    dict(defName="ArtificialIntelligence", label="artificial intelligence", track="InformationComputation", prereqs=["TheInternet", "Semiconductors"],
         desc="A network-scale learning system reasons and decides without being told each answer in advance."),
    dict(defName="NeuralInterfaces", label="neural interfaces", track="InformationComputation", prereqs=["ArtificialIntelligence", "Biochemistry"],
         desc="A direct link between nervous tissue and a machine lets thought itself drive a computer's input."),
    dict(defName="QuantumComputing", label="quantum computing", track="InformationComputation", prereqs=["Semiconductors", "ArtificialIntelligence"],
         desc="Qubits held in superposition solve certain problems no classical processor could finish in a lifetime."),
    dict(defName="ArchotechSeeds", label="archotech seeds", track="FrontierExotic", prereqs=["ArtificialIntelligence"],
         desc="A self-improving intelligence, seeded and left to recurse, aims at a technology no human team designed."),
    dict(defName="NeuralLace", label="neural lace", track="FrontierExotic", prereqs=["NeuralInterfaces"],
         desc="A mesh grown through living brain tissue turns a neural interface from a socket into a permanent part of the mind."),
    dict(defName="OrbitalRingConstruction", label="orbital ring construction", track="FrontierExotic", prereqs=["OrbitalHabitats", "AsteroidMining"],
         desc="A structure encircling the whole planet, built from mined asteroid material, replaces rockets with an elevator."),
    dict(defName="TerraformingEngineering", label="terraforming engineering", track="FrontierExotic", prereqs=["AsteroidMining", "Xenobotany"],
         desc="Engineered atmospheres and seeded xenobotany work together to make a dead world breathable."),
    dict(defName="WormholeTheory", label="wormhole theory", track="FrontierExotic", prereqs=["QuantumComputing"],
         desc="Quantum computation stable enough to model exotic spacetime finally puts a traversable shortcut within theoretical reach."),
    dict(defName="MindUploading", label="mind uploading", track="FrontierExotic", prereqs=["NeuralLace", "ArchotechSeeds"],
         desc="A neural lace fine enough to read a mind, and an archotech vast enough to hold it, make a person's continuity a software problem."),
    dict(defName="TranscendentIntelligence", label="transcendent intelligence", track="FrontierExotic", prereqs=["MindUploading"],
         desc="A mind uploaded, copied, and run past every biological limit stops being describable as human at all."),
    dict(defName="PostSingularityCivilization", label="post-singularity civilization", track="FrontierExotic", prereqs=["TranscendentIntelligence"],
         desc="A civilization built by and for transcendent minds no longer resembles anything the authored ladder set out to describe."),
    dict(defName="OpenEndedFrontier", label="the open-ended frontier", track="FrontierExotic", prereqs=["PostSingularityCivilization"],
         desc="Beyond this point the authored ladder ends, and whatever comes next has to be discovered rather than written down."),
]

PROJECTS_BY_ERA = {
    "SticksAndStones": ERA_STICKSANDSTONES,
    "Agrarian": ERA_AGRARIAN,
    "Bronze": ERA_BRONZE,
    "Classical": ERA_CLASSICAL,
    "Medieval": ERA_MEDIEVAL,
    "Industrial": ERA_INDUSTRIAL,
    "Information": ERA_INFORMATION,
    "Exotic": ERA_EXOTIC,
}

# ---------------------------------------------------------------------------
# Assembly
# ---------------------------------------------------------------------------


def build_flat_list():
    """Flattens PROJECTS_BY_ERA into one list of project dicts, each stamped with its era."""
    flat = []
    for era_name in ERA_NAMES_BY_ORDER:
        for proj in PROJECTS_BY_ERA[era_name]:
            p = dict(proj)
            p["era"] = era_name
            flat.append(p)
    return flat


def compute_depths(projects_by_name):
    """
    Longest chain ending at each project (root = depth 1), memoized DFS. Returns {defName: depth}.
    Assumes the caller already checked the graph is acyclic.
    """
    depth_cache = {}

    def depth_of(name):
        if name in depth_cache:
            return depth_cache[name]
        proj = projects_by_name[name]
        prereqs = proj["prereqs"]
        if not prereqs:
            depth_cache[name] = 1
            return 1
        depth_cache[name] = 1 + max(depth_of(p) for p in prereqs)
        return depth_cache[name]

    for name in projects_by_name:
        depth_of(name)
    return depth_cache


def assign_costs(projects_by_name, depths):
    """
    Cost inside each era's band, scaled by the project's depth relative to the shallowest and
    deepest project in its own era (deeper chains cost more). Rounded to the nearest 10.
    """
    by_era = defaultdict(list)
    for name, proj in projects_by_name.items():
        by_era[proj["era"]].append(name)

    costs = {}
    for era_name, names in by_era.items():
        lo, hi = ERA_COST_BAND[era_name]
        era_depths = [depths[n] for n in names]
        dmin, dmax = min(era_depths), max(era_depths)
        for n in names:
            if dmax == dmin:
                frac = 0.5
            else:
                frac = (depths[n] - dmin) / (dmax - dmin)
            raw = lo + frac * (hi - lo)
            cost = int(round(raw / 10.0)) * 10
            cost = max(lo, min(hi, cost))
            costs[n] = float(cost)
    return costs


def assign_view_coords(projects_by_name):
    """
    researchViewX = era order * 10 + slot, where slot orders projects within an era by track row
    then by their original authoring order. researchViewY = the project's track row.
    """
    by_era = defaultdict(list)
    for name, proj in projects_by_name.items():
        by_era[proj["era"]].append(name)

    coords = {}
    for era_name, names in by_era.items():
        order = ERA_ORDER[era_name]
        ordered = sorted(names, key=lambda n: (TRACK_ROW[projects_by_name[n]["track"]], names.index(n)))
        for slot, name in enumerate(ordered):
            x = order * 10 + slot
            y = TRACK_ROW[projects_by_name[name]["track"]]
            coords[name] = (float(x), float(y))
    return coords


def validate(projects_by_name):
    """Raises SystemExit with a clear message on the first validation failure found."""
    errors = []

    # Unique defNames (dict construction already enforces this at the language level for exact
    # duplicates within one era's list, but not across eras -- check explicitly.)
    seen_names = set()
    for era_name in ERA_NAMES_BY_ORDER:
        for proj in PROJECTS_BY_ERA[era_name]:
            name = proj["defName"]
            if name in seen_names:
                errors.append(f"duplicate defName: {name}")
            seen_names.add(name)

    # Unique labels.
    labels_seen = {}
    for name, proj in projects_by_name.items():
        label = proj["label"]
        if label in labels_seen and labels_seen[label] != name:
            errors.append(f"duplicate label {label!r}: {labels_seen[label]} and {name}")
        labels_seen[label] = name

    # Every prerequisite exists, is not self, and known track.
    for name, proj in projects_by_name.items():
        if proj["track"] not in KNOWN_TRACKS:
            errors.append(f"{name}: unknown track {proj['track']!r}")
        if not proj.get("desc"):
            errors.append(f"{name}: missing description")
        for prereq in proj["prereqs"]:
            if prereq == name:
                errors.append(f"{name}: lists itself as a prerequisite")
            elif prereq not in projects_by_name:
                errors.append(f"{name}: prerequisite {prereq!r} does not exist")

    # Prerequisite era order <= project's era order (same-or-earlier).
    for name, proj in projects_by_name.items():
        proj_order = ERA_ORDER[proj["era"]]
        for prereq in proj["prereqs"]:
            if prereq not in projects_by_name:
                continue  # already reported above
            prereq_order = ERA_ORDER[projects_by_name[prereq]["era"]]
            if prereq_order > proj_order:
                errors.append(
                    f"{name} ({proj['era']}): prerequisite {prereq} is from a later era ({projects_by_name[prereq]['era']})"
                )

    # No cycles (Tarjan-ish DFS coloring).
    WHITE, GRAY, BLACK = 0, 1, 2
    color = {n: WHITE for n in projects_by_name}

    def visit(n, stack):
        color[n] = GRAY
        stack.append(n)
        for prereq in projects_by_name[n]["prereqs"]:
            if prereq not in projects_by_name:
                continue
            if color[prereq] == GRAY:
                cycle = stack[stack.index(prereq):] + [prereq]
                errors.append("prerequisite cycle: " + " -> ".join(cycle))
            elif color[prereq] == WHITE:
                visit(prereq, stack)
        stack.pop()
        color[n] = BLACK

    for n in projects_by_name:
        if color[n] == WHITE:
            visit(n, [])

    # Every non-root project has >= 1 prerequisite; roots only in the first era.
    first_era = ERA_NAMES_BY_ORDER[0]
    for name, proj in projects_by_name.items():
        if not proj["prereqs"] and proj["era"] != first_era:
            errors.append(f"{name} ({proj['era']}): a root project outside the first era ({first_era})")

    # Per-era and total counts.
    counts = defaultdict(int)
    for proj in projects_by_name.values():
        counts[proj["era"]] += 1
    total = sum(counts.values())
    if total < 200:
        errors.append(f"total project count {total} is below the 200 minimum")
    for era_name in ERA_NAMES_BY_ORDER:
        if counts[era_name] < MIN_PROJECTS_PER_ERA:
            errors.append(f"era {era_name} has {counts[era_name]} projects, below the {MIN_PROJECTS_PER_ERA} minimum")
    for era_name in MIDDLE_ERAS:
        if counts[era_name] < MIDDLE_ERA_MIN:
            errors.append(f"middle era {era_name} has {counts[era_name]} projects, below the {MIDDLE_ERA_MIN} minimum")

    if errors:
        sys.stderr.write("gen_techtree: validation failed:\n")
        for e in errors:
            sys.stderr.write("  - " + e + "\n")
        sys.exit(1)


def xml_escape(text):
    return saxutils.escape(text, {'"': "&quot;"})


def render_era_xml(era_name, names_in_era, projects_by_name, costs, coords):
    order = ERA_ORDER[era_name]
    tech_level = ERA_TECH_LEVEL[era_name]
    lines = []
    lines.append('<?xml version="1.0" encoding="utf-8"?>')
    lines.append("<Defs>")
    lines.append("")
    lines.append(
        f"  <!-- Generated by tools/content/gen_techtree.py; do not hand-edit. Era: {era_name} "
        f"(order {order}, techLevel {tech_level}). Edit the PROJECTS_BY_ERA table and re-run the script instead. -->"
    )
    lines.append("")

    # Stable, readable ordering: by track row, then original authoring order within the track.
    ordered = sorted(
        names_in_era,
        key=lambda n: (TRACK_ROW[projects_by_name[n]["track"]], names_in_era.index(n)),
    )

    for name in ordered:
        proj = projects_by_name[name]
        x, y = coords[name]
        cost = costs[name]
        tags = [era_name, proj["track"]]
        if era_name == "Exotic":
            tags.append("Frontier")

        lines.append("  <ResearchProjectDef>")
        lines.append(f"    <defName>{name}</defName>")
        lines.append(f"    <label>{xml_escape(proj['label'])}</label>")
        lines.append(f"    <description>{xml_escape(proj['desc'])}</description>")
        lines.append(f"    <baseCost>{cost:g}</baseCost>")
        lines.append(f"    <techLevel>{tech_level}</techLevel>")
        lines.append("    <tab>Main</tab>")
        lines.append(f"    <era>{era_name}</era>")
        lines.append("    <tags>")
        for tag in tags:
            lines.append(f"      <li>{tag}</li>")
        lines.append("    </tags>")
        if proj["prereqs"]:
            lines.append("    <prerequisites>")
            for prereq in proj["prereqs"]:
                lines.append(f"      <li>{prereq}</li>")
            lines.append("    </prerequisites>")
        lines.append(f"    <researchViewX>{x:g}</researchViewX>")
        lines.append(f"    <researchViewY>{y:g}</researchViewY>")
        if name in LETTERS:
            title, text = LETTERS[name]
            lines.append(f"    <discoveredLetterTitle>{xml_escape(title)}</discoveredLetterTitle>")
            lines.append(f"    <discoveredLetterText>{xml_escape(text)}</discoveredLetterText>")
        lines.append("  </ResearchProjectDef>")
        lines.append("")

    lines.append("</Defs>")
    return "\n".join(lines) + "\n"


def main():
    repo_root = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
    out_dir = os.path.join(repo_root, "src", "SimWorld.Core", "Data", "Core", "Defs", "ResearchProjectDefs")

    flat = build_flat_list()
    projects_by_name = {p["defName"]: p for p in flat}
    if len(projects_by_name) != len(flat):
        # A duplicate defName collapsed the dict; validate() below will report it precisely.
        pass

    validate(projects_by_name)

    depths = compute_depths(projects_by_name)
    costs = assign_costs(projects_by_name, depths)
    coords = assign_view_coords(projects_by_name)

    if not os.path.isdir(out_dir):
        os.makedirs(out_dir)

    # Remove any stale generated files from a previous run (e.g. an era whose file name changed).
    prefix = "Research_"
    for existing in os.listdir(out_dir):
        if existing.startswith(prefix) and existing.endswith(".xml"):
            os.remove(os.path.join(out_dir, existing))

    per_era_names = defaultdict(list)
    for era_name in ERA_NAMES_BY_ORDER:
        for proj in PROJECTS_BY_ERA[era_name]:
            per_era_names[era_name].append(proj["defName"])

    for era_name in ERA_NAMES_BY_ORDER:
        order = ERA_ORDER[era_name]
        names_in_era = per_era_names[era_name]
        xml = render_era_xml(era_name, names_in_era, projects_by_name, costs, coords)
        file_name = f"Research_{order}_{era_name}.xml"
        with open(os.path.join(out_dir, file_name), "w", encoding="utf-8", newline="\n") as f:
            f.write(xml)

    # --- Summary ---
    counts_by_era = {e: len(per_era_names[e]) for e in ERA_NAMES_BY_ORDER}
    counts_by_track = defaultdict(int)
    for proj in projects_by_name.values():
        counts_by_track[proj["track"]] += 1
    roots = [n for n, p in projects_by_name.items() if not p["prereqs"]]
    max_depth = max(depths.values())
    deepest = [n for n, d in depths.items() if d == max_depth]

    print("=== SimWorld tech tree: generation summary ===")
    print(f"Total projects: {len(projects_by_name)}")
    print("Projects per era:")
    for e in ERA_NAMES_BY_ORDER:
        print(f"  {e:16s} {counts_by_era[e]:3d}")
    print("Projects per track:")
    for t, _row in TRACKS:
        print(f"  {t:24s} {counts_by_track.get(t, 0):3d}")
    print(f"Roots ({len(roots)}): {', '.join(sorted(roots))}")
    print(f"Max prerequisite chain depth: {max_depth} (reached by: {', '.join(sorted(deepest))})")
    print(f"Wrote {len(ERA_NAMES_BY_ORDER)} files to {out_dir}")


if __name__ == "__main__":
    main()
