
from os import O_APPEND
from typing import OrderedDict


files = ['Properties\\AssemblyInfo.cs', 'Adder.cs', 'Adder.Designer.cs']
files.extend(['Player.cs'])

merged = []
usings = []
result = []
first_using = False
first_using_index = 0

for fn in files:
  print ('processing ' + fn)
  found_namespace = False
  found_ns_bracket = False
  first_line = True
  f = open(fn, 'r', encoding="utf8")
  merged.append('#region ' + fn + '\n')
  linenum = 0
  for ln in f:
    linenum += 1
    if first_line:
      first_line = False
      # first line might contain the byte order mark
      # just remove it
      if ln.startswith('\ufeff'):
        ln = ln[1:]
    if ln.startswith('using') and ';' in ln:
      usings.append(ln)
      if not first_using:
        first_using = True
        first_using_index = linenum-1
    else:
      merged.append(ln)

  f.close()
  merged.append('#endregion ' + fn + '\n')

# merge the usings, remove duplicates
nodups = list(OrderedDict.fromkeys(usings))

print (str(len(nodups)), "usings")
print (str(len(merged)), "code lines")

# construct the final file
if first_using_index > 0:
  # if there is 'stuff' before the initial using, do that first
  result = merged[0:first_using_index]
  result.extend(nodups)
  result.extend(merged[first_using_index:])
else:
  # first line of the first file was a using
  result.extend(nodups)
  result.extend(merged)
print (str(len(result)), 'file lines')

file_out = open("single_source\\Adder.cs","w", encoding="utf8")
file_out.writelines(result)
file_out.close()


