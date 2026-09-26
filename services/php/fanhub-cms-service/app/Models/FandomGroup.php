<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Factories\HasFactory;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\HasMany;
use Illuminate\Support\Str;

class FandomGroup extends Model
{
    use HasFactory;

    protected $table = 'fandom_groups';

    protected $keyType = 'string';
    public $incrementing = false;

    protected $fillable = [
        'id',
        'name',
        'description',
        'status',
        'created_by',
    ];

    protected $appends = [
        'title',
    ];

    protected static function booted(): void
    {
        static::creating(function (FandomGroup $group) {
            if (empty($group->id)) {
                $group->id = 'req_' . Str::lower(Str::random(12));
            }
            if (empty($group->status)) {
                $group->status = 'active';
            }
        });
    }

    public function getTitleAttribute(): ?string
    {
        return $this->attributes['name'] ?? null;
    }

    public function setTitleAttribute(?string $value): void
    {
        $this->attributes['name'] = $value;
    }

    public function members(): HasMany
    {
        return $this->hasMany(FandomGroupMember::class, 'group_id');
    }

    public function posts(): HasMany
    {
        return $this->hasMany(Post::class, 'group_id');
    }
}
