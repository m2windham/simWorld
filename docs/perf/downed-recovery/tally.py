"""Tally a paired recovery run by cause of death.

Reads the outputs of `Pair.dll load` for one world seed and a list of continuation seeds, both arms, and prints
per continuation and in total: how many of the saved raid's casualties survived, and what killed the rest -
wound infection, blood loss, or anything else (a later raid, a manhunter pack, an earthquake). Also counts the
new threats the storyteller fired after the save, because the two arms' storytellers diverge at the first
infection roll and a settlement that keeps its people can draw more of them.

Usage: python3 tally.py <dir> <worldSeed> <contSeed>...
Files read: <dir>/w<worldSeed>-c<contSeed>-before.txt and ...-after.txt
"""
import re
import sys


def tally(path):
    text = open(path).read()
    casualties = int(re.search(r'PAIR casualties=(\d+)', text).group(1))
    dead = re.findall(r'  (\w+)#(\d+): DEAD (\w+) \(([\w-]+)\)', text)
    infection = sum(1 for d in dead if d[3] == 'WoundInfection')
    blood = sum(1 for d in dead if d[3] == 'BloodLoss')
    threats = len(re.findall(r'FIRED (RaidEnemy|ManhunterPack|Earthquake)', text))
    return dict(casualties=casualties, survived=casualties - len(dead), infection=infection,
                bloodLoss=blood, other=len(dead) - infection - blood, newThreats=threats)


def main():
    folder, world, conts = sys.argv[1], sys.argv[2], sys.argv[3:]
    for arm in ('before', 'after'):
        total = None
        print('==', arm)
        for c in conts:
            row = tally('%s/w%s-c%s-%s.txt' % (folder, world, c, arm))
            print('  c%s: %s' % (c, row))
            total = row if total is None else {k: total[k] + row[k] for k in total}
        print('  TOTAL: %s' % total)


if __name__ == '__main__':
    main()
