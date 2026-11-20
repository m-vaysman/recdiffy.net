#!/usr/bin/env perl
# Builds the docs for AI agents (#530) into a site folder, from the repository root:
#
#   perl .github/scripts/llms-docs.pl <site-dir>
#
# 1. Copies every llms.txt and Markdown page under docs/, and docs/llms.txt to the site root as well. Relative links become
#    absolute: a page the site serves links to the site, anything else to GitHub.
# 2. Slices the long reference docs into one page per heading, under docs/reference/<name>/, with a generated llms.txt
#    listing them, so an agent fetches the one section it needs. The slices are built from the docs on this commit, so
#    they never drift from them.
# 3. Joins every page into llms-full.txt.
# 4. Fails when a link to the site or to this repository's main branch points at nothing. CI runs it on every pull request,
#    so a renamed heading or a deleted file fails the pull request, not the deploy.
use strict;
use warnings;
use File::Basename qw(dirname);
use File::Find qw(find);
use File::Path qw(make_path);

my $site = shift or die "usage: perl .github/scripts/llms-docs.pl <site-dir>\n";
my $repo = 'm-vaysman/recdiffy.net';
my $pages = 'https://m-vaysman.github.io/recdiffy.net';
my $blob = "https://github.com/$repo/blob/main";
my $raw = "https://raw.githubusercontent.com/$repo/main";

# The long docs, in llms-full.txt order, and the folder each one's sections go to under docs/reference/.
my @sliced = (
    [ 'README.md',                             'readme' ],
    [ 'RecDiffy.NET/docs/PROFILES.md',         'profiles' ],
    [ 'RecDiffy.NET/docs/RESULTS.md',          'results' ],
    [ 'RecDiffy.NET/docs/FIX-RECIPE.md',       'fix-recipe' ],
    [ 'RecDiffy.NET/docs/PROFILE-EXAMPLES.md', 'profile-examples' ],
    [ 'RecDiffy.NET/docs/RESULT-EXAMPLES.md',  'result-examples' ],
);

sub read_file {
    my ($path) = @_;
    open my $in, '<:raw', $path or die "can't read $path: $!\n";
    local $/;
    my $text = <$in>;
    $text =~ s/\r\n/\n/g;
    return $text;
}

sub write_file {
    my ($path, $text) = @_;
    make_path(dirname($path));
    open my $out, '>:raw', $path or die "can't write $path: $!\n";
    print {$out} $text;
}

# Resolves . and .. in a repository path.
sub normalize {
    my @parts;
    for my $part (split m{/}, shift) {
        next if $part eq '' || $part eq '.';
        if ($part eq '..') { pop @parts } else { push @parts, $part }
    }
    return join '/', @parts;
}

# "path#anchor" of each heading in a sliced doc -> the URL of its section page, and "path" -> its index of sections.
# Filled before any page is written.
my %section_url;

# Where a repository file is read on the web: the section page for a sliced doc's heading, the site for a page the site
# serves, and GitHub for everything else.
sub url_of {
    my ($path, $anchor) = @_;
    $anchor //= '';
    return $section_url{"$path$anchor"} if $section_url{"$path$anchor"};
    return ($path =~ m{^docs/.*(?:\.md|llms\.txt)$} ? "$pages/$path" : "$blob/$path") . $anchor;
}

# Makes a Markdown file's relative links absolute, so they still work when the file is served from somewhere else.
sub absolute_links {
    my ($text, $from) = @_;
    my $dir = dirname($from);
    $text =~ s{\]\((?!https?://|mailto:)([^)\s]*)\)}{
        my ($path, $anchor) = $1 =~ /^([^#]*)(#.*)?$/;
        '](' . url_of($path eq '' ? $from : normalize("$dir/$path"), $anchor) . ')';
    }ge;
    return $text;
}

# The anchor GitHub gives a heading.
sub slug {
    my $slug = lc shift;
    $slug =~ s/[^a-z0-9 _-]//g;
    $slug =~ s/ /-/g;
    return $slug;
}

# Splits a doc into one section per ## and ### heading. A ## section holds its ### sections too. Fenced code is skipped.
sub sections {
    my ($text) = @_;
    my (@sections, $h2, $h3, $fenced);
    for my $line (split /\n/, $text) {
        $fenced = !$fenced if $line =~ /^```/;
        if (!$fenced && $line =~ /^(##|###) (.+)$/) {
            my $section = { level => length $1, title => $2, slug => slug($2), lines => [] };
            push @sections, $section;
            $h2 = $section if $section->{level} == 2;
            $h3 = $section->{level} == 3 ? $section : undef;
        }
        push @{ $h2->{lines} }, $line if $h2;
        push @{ $h3->{lines} }, $line if $h3;
    }
    return @sections;
}

for my $doc (@sliced) {
    my ($path, $name) = @$doc;
    $section_url{"$path#$_->{slug}"} = "$pages/docs/reference/$name/$_->{slug}.md" for sections(read_file($path));
    $section_url{$path} = "$pages/docs/reference/$name/llms.txt";
}

my @written;

sub publish {
    my ($path, $text) = @_;
    write_file("$site/$path", $text);
    push @written, $path;
}

# 1. The pages under docs/.
my @pages;
find({ no_chdir => 1, wanted => sub { push @pages, $File::Find::name if -f && m{(?:\.md|/llms\.txt)$} } }, 'docs');
@pages = sort { ($a ne 'docs/agents.md') <=> ($b ne 'docs/agents.md') or $a cmp $b } @pages;
for my $page (@pages) {
    my $text = read_file($page);
    publish($page, $page =~ /\.md$/ ? absolute_links($text, $page) : $text);
}
publish('llms.txt', read_file('docs/llms.txt'));

# 2. The long docs, one page per section.
for my $doc (@sliced) {
    my ($path, $name) = @$doc;
    my $text = read_file($path);
    my ($title) = $text =~ /^# (.+)$/m;
    $title //= $path;
    my (%seen, @index);
    for my $section (sections($text)) {
        my $slug = $section->{slug};
        next if $slug eq 'contents';
        die "$path: two headings give the anchor '$slug'\n" if $seen{$slug}++;
        my $body = join "\n", @{ $section->{lines} };
        publish("docs/reference/$name/$slug.md",
            "> A section of [$title]($blob/$path#$slug). All its sections: $pages/docs/reference/$name/llms.txt\n\n"
            . absolute_links($body, $path) . "\n");
        my $indent = $section->{level} == 3 ? '  ' : '';
        push @index, "$indent- [$section->{title}]($pages/docs/reference/$name/$slug.md)";
    }
    publish("docs/reference/$name/llms.txt",
        "# $title\n\n> Every section of $path, one page each. The whole page: $raw/$path\n\n## Sections\n\n"
        . join("\n", @index) . "\n");
}

# 3. Everything in one file.
my $sha = substr($ENV{GITHUB_SHA} // 'local', 0, 7);
my $full = "# RecDiffy.NET: the documentation in one file\n\n"
    . "> Built from $repo\@$sha. The index is $pages/docs/llms.txt. Each part starts with the URL it was taken from.\n";
for my $path ((grep { /\.md$/ } @pages), map { $_->[0] } @sliced) {
    my $source = $path =~ m{^docs/} ? "$pages/$path" : "$blob/$path";
    $full .= "\n\n---\n\nSource: $source\n\n" . absolute_links(read_file($path), $path);
}
publish('llms-full.txt', $full);

# 4. Every link to the site or to main must point at something.
my %missing;
for my $path (@written) {
    my $text = read_file("$site/$path");
    while ($text =~ m{\Q$pages\E/([^\s)#>"`]*)}g) {
        (my $target = $1) =~ s/[.,;:]+$//;
        next unless $target =~ m{^(?:docs/|llms)};
        $missing{"$path -> $pages/$target"} = 1 unless -e "$site/$target";
    }
    while ($text =~ m{(?:\Q$raw\E|\Q$blob\E)/([^\s)#>"`]+)}g) {
        (my $target = $1) =~ s/[.,;:]+$//;
        $missing{"$path -> $target"} = 1 unless -e $target;
    }
}
if (%missing) {
    print STDERR "Links that point at nothing:\n", map { "  $_\n" } sort keys %missing;
    exit 1;
}
printf "Wrote %d files to %s; every link resolves.\n", scalar @written, $site;
